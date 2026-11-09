using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using Polly.CircuitBreaker;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

/// <summary>
/// An answer of Alanube: its status and its body as it came.
/// </summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body.</param>
public sealed record AlanubeAnswer(HttpStatusCode Status, string Body)
{
    /// <summary>
    /// Whether Alanube accepted the request.
    /// </summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>
    /// The body as JSON.
    /// </summary>
    /// <returns>The body, or null when it is empty or not JSON.</returns>
    public JsonNode? Json()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(Body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The body as a JSON object.
    /// </summary>
    /// <returns>The body, or null when it is not an object.</returns>
    public JsonObject? AsObject() => Json() as JsonObject;

    /// <summary>
    /// The errors of the body (<c>{"errors":[{"code","message"}]}</c> or the bare array), as they came.
    /// </summary>
    /// <returns>The code (empty when it has none) and the message of each error.</returns>
    public IReadOnlyList<(string Code, string Message)> Errors()
    {
        var root = Json();
        var list = root is JsonArray array ? array : (root as JsonObject)?["errors"] as JsonArray;

        if (list is null)
        {
            var message = TextOf((root as JsonObject)?["message"]);

            return string.IsNullOrWhiteSpace(message) ? [] : [(string.Empty, message)];
        }

        return [.. list.OfType<JsonObject>().Select(x => (TextOf(x["code"]), TextOf(x["message"])))];
    }

    private static string TextOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? string.Empty;
}

/// <summary>
/// The calls to the API of Alanube, with the token of the main company.
/// </summary>
public interface IAlanubeClient
{
    /// <summary>
    /// Sends a request.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and query, relative to the base of the API.</param>
    /// <param name="body">The body already written, if any: the idempotency key is derived from these same bytes.</param>
    /// <param name="token">The token of the main company.</param>
    /// <param name="idempotencyKey">The idempotency key of an emission, if any.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, when Alanube took the request or refused its content.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>301</c> when Alanube does not answer or fails, or <c>302</c> when it rejects the token.</exception>
    Task<AlanubeAnswer> SendAsync(HttpMethod method, string path, string? body, string token, string? idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a file through the signed link Alanube gave. The link is the authorization: no token goes with it.
    /// </summary>
    /// <param name="url">The signed link.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or null when the file is not there.</returns>
    Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// The calls to the API of Alanube (<c>docs/alanube/docs/respuestas.md</c> and <c>errores.md</c>): 401 and 403 are a
/// rejected token; 429, 5xx (also <c>EPR500</c> and <c>EPR503</c>, the tax authority not answering), a timeout or an
/// open circuit are Alanube not answering, which the status job retries with its backoff; the rest is an answer that the
/// adapter reads.
/// </summary>
/// <remarks>
/// The sends go through a client without retries: the next round sends again with the same idempotency key, and
/// Alanube answers what the first one got. The files go through their own client with no token at all: the signed link
/// redirects to the storage, which refuses a foreign authorization. Nothing of the body is logged: it carries the
/// buyer's data.
/// </remarks>
/// <param name="httpClientFactory">The factory of the HTTP clients.</param>
/// <param name="logger">The logger.</param>
public sealed class AlanubeClient(IHttpClientFactory httpClientFactory, ILogger<AlanubeClient> logger) : IAlanubeClient
{
    /// <summary>
    /// The client of the reads, with retries.
    /// </summary>
    public const string ReadsClientName = "Alanube";

    /// <summary>
    /// The client of the sends, without retries.
    /// </summary>
    public const string WritesClientName = "Alanube.Writes";

    /// <summary>
    /// The client of the files.
    /// </summary>
    public const string FilesClientName = "Alanube.Files";

    /// <summary>
    /// The header of the idempotency key.
    /// </summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <inheritdoc/>
    public async Task<AlanubeAnswer> SendAsync(HttpMethod method, string path, string? body, string token, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(method == HttpMethod.Get ? ReadsClientName : WritesClientName);

        using var request = new HttpRequestMessage(method, path.TrimStart('/'));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException or BrokenCircuitException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Alanube {Method} {Path} did not answer: {Error}", method, PathOf(path), exception.GetType().Name);

            throw Unavailable();
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogInformation("Alanube {Method} {Path} answered {Status}", method, PathOf(path), (int)response.StatusCode);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderCredentialsRejected.GetCode(), Errors.ProviderCredentialsRejected.GetMessage());
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw Unavailable();
            }

            return new AlanubeAnswer(response.StatusCode, text);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var client = httpClientFactory.CreateClient(FilesClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Only the path: the query of a signed link is its authorization.
            logger.LogWarning("Alanube file {Path} answered {Status}", url.AbsolutePath, (int)response.StatusCode);

            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static CodeDesignPlusException Unavailable() => new(Layer.Infrastructure, Errors.ProviderUnavailable.GetCode(), Errors.ProviderUnavailable.GetMessage());

    // The path without the query: a lookup carries the number of the document, not a secret, but nothing of the request
    // is needed in the log beyond where it went.
    private static string PathOf(string path) => path.Split('?')[0];
}
