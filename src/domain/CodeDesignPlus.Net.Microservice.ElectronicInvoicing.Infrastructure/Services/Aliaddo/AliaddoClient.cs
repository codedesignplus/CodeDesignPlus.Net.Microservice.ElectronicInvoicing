using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using Polly.CircuitBreaker;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// An answer of Aliaddo: its status and its body as it came.
/// </summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body.</param>
public sealed record AliaddoAnswer(HttpStatusCode Status, string Body)
{
    /// <summary>
    /// Whether it is a refusal of the content (a validation of Aliaddo): it is not retried.
    /// </summary>
    public bool IsValidationRefusal => Status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    /// <summary>
    /// Reads the body.
    /// </summary>
    /// <typeparam name="T">The type of the body.</typeparam>
    /// <returns>The body, or the default when it is empty.</returns>
    public T? Read<T>() => string.IsNullOrWhiteSpace(Body) || Body.Trim() == "null" ? default : System.Text.Json.JsonSerializer.Deserialize<T>(Body, AliaddoClient.Json);
}

/// <summary>
/// The calls to the API of Aliaddo Nitro, with the token of the organization.
/// </summary>
public interface IAliaddoClient
{
    /// <summary>
    /// Sends a request.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and query, relative to the base of the API.</param>
    /// <param name="body">The body, if any.</param>
    /// <param name="token">The token of the organization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, when Aliaddo accepted the request or refused its content.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>301</c> when Aliaddo does not answer or fails, or <c>302</c> when it rejects the token.</exception>
    Task<AliaddoAnswer> SendAsync(HttpMethod method, string path, object? body, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a file of a document.
    /// </summary>
    /// <param name="url">The URL of the file.</param>
    /// <param name="token">The token of the organization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or null when the file is not there.</returns>
    Task<byte[]?> DownloadAsync(Uri url, string token, CancellationToken cancellationToken);
}

/// <summary>
/// The calls to the API of Aliaddo Nitro (<c>docs/aliaddo/docs/codigos-de-respuesta-de-la-api.md</c>): 401 and 403 are
/// a rejected token; 429, 5xx and 520, a timeout or an open circuit are Aliaddo not answering, which the status job
/// retries with its backoff; 400 and 422 are a refusal of the content, which is not retried.
/// </summary>
/// <remarks>
/// The sends go through a client without retries: a send that timed out may have arrived, and sending it again on the
/// spot could issue twice; the next round looks it up by its number first. The reads retry with the resilience of the
/// configuration. Nothing of the body is logged: it carries the buyer's data.
/// </remarks>
/// <param name="httpClientFactory">The factory of the HTTP clients.</param>
/// <param name="logger">The logger.</param>
public sealed class AliaddoClient(IHttpClientFactory httpClientFactory, ILogger<AliaddoClient> logger) : IAliaddoClient
{
    /// <summary>
    /// The client of the reads, with retries.
    /// </summary>
    public const string ReadsClientName = "Aliaddo";

    /// <summary>
    /// The client of the sends, without retries.
    /// </summary>
    public const string WritesClientName = "Aliaddo.Writes";

    /// <summary>
    /// The client of the files.
    /// </summary>
    public const string FilesClientName = "Aliaddo.Files";

    /// <summary>
    /// How the bodies are read and written: case-insensitive, because the answers come in PascalCase.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public async Task<AliaddoAnswer> SendAsync(HttpMethod method, string path, object? body, string token, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(method == HttpMethod.Get ? ReadsClientName : WritesClientName);

        using var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException or BrokenCircuitException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Aliaddo {Method} {Path} did not answer: {Error}", method, PathOf(path), exception.GetType().Name);

            throw Unavailable();
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogInformation("Aliaddo {Method} {Path} answered {Status}", method, PathOf(path), (int)response.StatusCode);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderCredentialsRejected.GetCode(), Errors.ProviderCredentialsRejected.GetMessage());
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw Unavailable();
            }

            return new AliaddoAnswer(response.StatusCode, text);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> DownloadAsync(Uri url, string token, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(FilesClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // TODO(pendings/663): the documentation does not say whether the files need the token; it is sent only to the host
        // of the API, never to a storage of another host.
        if (client.BaseAddress is not null && url.Host == client.BaseAddress.Host)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Aliaddo file {Path} answered {Status}", url.AbsolutePath, (int)response.StatusCode);

            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static CodeDesignPlusException Unavailable() => new(Layer.Infrastructure, Errors.ProviderUnavailable.GetCode(), Errors.ProviderUnavailable.GetMessage());

    // The path without the query: the query of a lookup carries the number of the document, not a secret, but nothing of
    // the request is needed in the log beyond where it went.
    private static string PathOf(string path) => path.Split('?')[0];
}
