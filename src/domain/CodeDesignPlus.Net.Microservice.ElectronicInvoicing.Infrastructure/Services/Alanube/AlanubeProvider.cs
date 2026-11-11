using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

/// <summary>
/// The adapter of Alanube (the API of Alegra as electronic provider): the platform's main company creates and operates
/// one associated company per issuer, this service gives the numbers, and the emission is synchronous.
/// </summary>
/// <remarks>
/// <para>
/// Built from <c>docs/alanube/</c> and checked against the sandbox on 2026-11-09 (the fixtures of its tests are those
/// answers).
/// </para>
/// <para>
/// <b>Answers.</b> A send answers with the result of the tax authority: <c>ACCEPTED</c> and
/// <c>ACCEPTED_WITH_OBSERVATIONS</c> are validated (the observations are kept), <c>REJECTED</c> is rejected with its
/// rules, and a send without a legal status is still processing and is asked about later. A refusal of the content
/// (400, 404, 422) is a rejection of the provider, except the codes of <see cref="AlanubeErrorCodesOptions"/>: an issuer
/// not ready makes the document wait and counts towards suspending the issuer, and a number already received is looked
/// up by its number and adopted.
/// </para>
/// <para>
/// <b>Idempotency.</b> Every send carries an <c>Idempotency-Key</c> derived from the document
/// (<see cref="AlanubeMapper.IdempotencyKeyOf"/>), so a send whose answer was lost is answered with the first result.
/// A <c>409</c> (the first send still running, or its result unknown) looks the document up by its number and, if it
/// is not there yet, waits for the next round.
/// </para>
/// <para>
/// <b>Files.</b> The links Alanube gives last an hour, so they are not kept: the document is read again and its files
/// downloaded at once. The PDF only travels inside the package (<c>files.zip</c>).
/// </para>
/// </remarks>
/// <param name="client">The calls to the API.</param>
/// <param name="options">The options of Alanube.</param>
/// <param name="logger">The logger.</param>
public sealed class AlanubeProvider(IAlanubeClient client, IOptions<AlanubeOptions> options, ILogger<AlanubeProvider> logger) : IIssuerProvisioningProvider, IProviderEventReader, IProviderTestSetRunner
{
    /// <summary>
    /// The code of the adapter.
    /// </summary>
    public const string ProviderCode = "Alanube";

    /// <summary>
    /// The types of document Alanube issues.
    /// </summary>
    public static readonly IReadOnlySet<ElectronicDocumentType> SupportedTypes = new HashSet<ElectronicDocumentType>([ElectronicDocumentType.Invoice, ElectronicDocumentType.CreditNote, ElectronicDocumentType.DebitNote, ElectronicDocumentType.PosTicket, ElectronicDocumentType.PosAdjustmentNote]);

    // The statuses of a test set (docs/alanube/api/reference-createtestset.md).
    private const string TestSetAccepted = "ACCEPTED";
    private static readonly HashSet<string> TestSetRejected = new(["REJECTED", "FAILED"], StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string Code => ProviderCode;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities => new(
        IssuerProvisioningMode.Api,
        CredentialScope.MasterAccount,
        NumberingMode.ByPlatform,
        HabilitationByApi: true,
        DocumentTypes: SupportedTypes,
        SupportsPrepayments: true,
        ExternalBillingReference: true,
        options.Value.Webhooks.Enabled ? StatusUpdatesMode.Push : StatusUpdatesMode.Poll,
        IdempotencyMode.Header,
        ProvidesXml: true,
        DeliversToBuyer: options.Value.NotifyBuyerByEmail,
        RequiresProviderCatalog: false)
    {
        // Alanube puts the day of Colombia it receives the document as its issue date (errors AEP18010 and AEP18011 of
        // docs/alanube); only the support document takes one.
        IssuerSetsIssueDate = false
    };

    /// <inheritdoc/>
    public async Task<CredentialsValidation> ValidateCredentialsAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await client.SendAsync(HttpMethod.Get, AlanubePaths.OwnCompany, null, Token(), null, cancellationToken);

            return answer.IsSuccess ? new CredentialsValidation(true, null) : new CredentialsValidation(false, ((int)answer.Status).ToString(CultureInfo.InvariantCulture));
        }
        catch (CodeDesignPlusException exception) when (exception.Code == Errors.ProviderCredentialsRejected.GetCode())
        {
            return new CredentialsValidation(false, exception.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<ProviderSubmission> SubmitAsync(ProviderIssuer issuer, ProviderDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(document);

        var companyId = CompanyIdOf(issuer);
        var problems = AlanubeMapper.Problems(document);

        // What Alanube would refuse is refused here, without spending a call.
        if (problems.Count > 0)
        {
            return new ProviderSubmission(null, new ProviderDocumentState(ProviderDocumentStatus.Rejected, null, null, document.Number, null, problems, RejectionOrigin.Provider, null));
        }

        var body = AlanubeMapper.ToRequest(issuer, document, options.Value).ToJsonString();
        var key = AlanubeMapper.IdempotencyKeyOf(document.DocumentId, document.Revision, body);

        var answer = await client.SendAsync(HttpMethod.Post, AlanubePaths.CreateOf(document.Type), body, Token(), key, cancellationToken);

        if (answer.IsSuccess)
        {
            var created = AlanubeDocument.From(answer.Json()) ?? throw NotUnderstood();

            return new ProviderSubmission(created.Id, created.StateOf(document.Number));
        }

        var errors = answer.Errors();
        var codes = errors.Select(x => x.Code).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var settings = options.Value.ErrorCodes;

        if (codes.Any(settings.IssuerNotReady.Contains))
        {
            logger.LogWarning("Alanube does not let issuer {IssuerId} issue {DocumentType}: {Codes}", issuer.IssuerId, document.Type, string.Join(',', codes));

            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderIssuerNotReady.GetCode(), Errors.ProviderIssuerNotReady.GetMessage());
        }

        // The first send is still running or its result is unknown, or the number was already received: what Alanube has
        // with the number is adopted; if it has nothing yet, the next round tries again with the same key.
        if (answer.Status == HttpStatusCode.Conflict || codes.Any(settings.AlreadyReceived.Contains))
        {
            var found = await FindByNumberAsync(companyId, document.Type, document.Number, cancellationToken);

            if (found is null)
            {
                logger.LogWarning("Alanube answered {Status} ({Codes}) for document {DocumentId} and does not list it by its number yet.", (int)answer.Status, string.Join(',', codes), document.DocumentId);

                throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderUnavailable.GetCode(), Errors.ProviderUnavailable.GetMessage());
            }

            return new ProviderSubmission(found.Id, found.StateOf(document.Number));
        }

        var reasons = errors.Select(x => string.IsNullOrWhiteSpace(x.Code) ? x.Message : $"{x.Code}: {x.Message}").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var raw = string.Join(',', new[] { ((int)answer.Status).ToString(CultureInfo.InvariantCulture) }.Concat(codes));

        return new ProviderSubmission(null, new ProviderDocumentState(ProviderDocumentStatus.Rejected, null, null, document.Number, null, reasons.Count > 0 ? reasons : [AlanubeFallbackReason(answer)], RejectionOrigin.Provider, raw));
    }

    /// <inheritdoc/>
    public async Task<ProviderDocumentState?> GetStateAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var found = await FindAsync(issuer, lookup, cancellationToken);

        return found?.StateOf(lookup.Number);
    }

    /// <inheritdoc/>
    public async Task<ProviderFiles> GetFilesAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        // Read again: the links of the answer of the send may have expired.
        var found = await FindAsync(issuer, lookup, cancellationToken);

        if (found is null)
        {
            return ProviderFiles.None;
        }

        var name = found.FullNumber ?? found.Number?.ToString() ?? lookup.Number?.ToString() ?? found.Id;

        var xml = await DownloadAsync(found.Files.Xml, $"{name}.xml", cancellationToken);
        var attached = await DownloadAsync(found.Files.AttachedDocument, $"{name}-attached.xml", cancellationToken);
        var package = found.Files.Zip is { } zip ? await client.DownloadAsync(zip, cancellationToken) : null;

        var pdf = Unzip(package, ".pdf", $"{name}.pdf");

        // The package carries the attached document too: it fills in when its own link failed.
        attached ??= Unzip(package, ".xml", $"{name}-attached.xml");

        return new ProviderFiles(xml, attached, pdf);
    }

    /// <inheritdoc/>
    public async Task<ProvisionedIssuer> ProvisionAsync(ProviderIssuer issuer, IssuerContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        var token = Token();
        var identification = issuer.TaxId.Split('-')[0];

        // A company created by a send whose answer was lost is adopted instead of creating a second one.
        var listed = await client.SendAsync(HttpMethod.Get, $"{AlanubePaths.Companies}?identification={Uri.EscapeDataString(identification)}", null, token, null, cancellationToken);
        var existing = listed.IsSuccess ? CompanyFrom(listed.Json(), identification) : null;

        if (existing is not null)
        {
            return new ProvisionedIssuer(existing.Value.Id, existing.Value.GovernmentStatus);
        }

        var body = AlanubeMapper.CompanyRequestOf(issuer, options.Value).ToJsonString();
        var answer = await client.SendAsync(HttpMethod.Post, AlanubePaths.Companies, body, token, null, cancellationToken);
        var created = answer.IsSuccess ? CompanyFrom(answer.Json(), null) : null;

        if (created is null)
        {
            logger.LogWarning("Alanube did not create the company of tenant {Tenant}: {Status} {Codes}", issuer.Tenant, (int)answer.Status, string.Join(',', answer.Errors().Select(x => x.Code)));

            throw answer.IsSuccess ? NotUnderstood() : new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderIssuerNotReady.GetCode(), Errors.ProviderIssuerNotReady.GetMessage());
        }

        return new ProvisionedIssuer(created.Value.Id, created.Value.GovernmentStatus);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderNumberingRange>> GetNumberingRangesAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        // Only in production: the sandbox answers AEP9006, and an issuer not enabled yet answers AEP9009.
        var answer = await client.SendAsync(HttpMethod.Get, $"resolutions/{Uri.EscapeDataString(issuer.TaxId.Split('-')[0])}", null, Token(), null, cancellationToken);

        if (!answer.IsSuccess || answer.AsObject()?["resolutions"] is not JsonArray resolutions)
        {
            return [];
        }

        return [.. resolutions.OfType<JsonObject>().Select(RangeOf).Where(x => x is not null).Select(x => x!)];
    }

    /// <inheritdoc/>
    public async Task<ProviderTestSet> RunTestSetAsync(ProviderIssuer issuer, ElectronicDocumentType type, string testSetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        var kind = AlanubeMapper.TestSetTypeOf(type) ?? throw new CodeDesignPlusException(Layer.Infrastructure, Errors.TestSetTypeNotSupported.GetCode(), Errors.TestSetTypeNotSupported.GetMessage());

        var body = new JsonObject
        {
            ["type"] = kind,
            ["governmentId"] = testSetId,
            ["company"] = new JsonObject { ["id"] = CompanyIdOf(issuer) }
        };

        var answer = await client.SendAsync(HttpMethod.Post, AlanubePaths.TestSets, body.ToJsonString(), Token(), null, cancellationToken);

        // A set already approved is refused (AEP4007) with the approved one in the answer: the issuer is enabled already.
        if (!answer.IsSuccess && answer.AsObject()?["approvedTestSet"]?["status"] is JsonValue approved && approved.TryGetValue<string>(out var approvedStatus) && string.Equals(approvedStatus, TestSetAccepted, StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderTestSet(ProviderDocumentStatus.Accepted, approvedStatus);
        }

        if (!answer.IsSuccess)
        {
            var codes = answer.Errors().Select(x => string.IsNullOrWhiteSpace(x.Code) ? x.Message : x.Code).ToList();

            logger.LogWarning("Alanube did not take the test set {Kind} of issuer {IssuerId}: {Status} {Codes}", kind, issuer.IssuerId, (int)answer.Status, string.Join(',', codes));

            return new ProviderTestSet(ProviderDocumentStatus.Rejected, string.Join(',', new[] { ((int)answer.Status).ToString(CultureInfo.InvariantCulture) }.Concat(codes)));
        }

        var status = (answer.AsObject()?["testSets"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(x => string.Equals(x["type"]?.GetValue<string>(), kind, StringComparison.OrdinalIgnoreCase))
            .Select(x => x["status"]?.GetValue<string>())
            .FirstOrDefault();

        if (string.Equals(status, TestSetAccepted, StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderTestSet(ProviderDocumentStatus.Accepted, status);
        }

        return new ProviderTestSet(status is not null && TestSetRejected.Contains(status) ? ProviderDocumentStatus.Rejected : ProviderDocumentStatus.Processing, status);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Alanube does not sign its calls: it sends the headers this service gave it when it created the company. The
    /// secret is compared in constant time, on its hash so its length does not leak either.
    /// </remarks>
    public bool IsAuthentic(string body, IReadOnlyDictionary<string, string> headers)
    {
        var webhooks = options.Value.Webhooks;

        if (!webhooks.Enabled || headers is null || string.IsNullOrWhiteSpace(webhooks.Secret) || string.IsNullOrWhiteSpace(webhooks.Header))
        {
            return false;
        }

        var received = headers.FirstOrDefault(x => string.Equals(x.Key, webhooks.Header, StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrEmpty(received))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(received)), SHA256.HashData(Encoding.UTF8.GetBytes(webhooks.Secret)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The body wraps the document in a property named after its type (<c>invoice</c>, <c>creditNote</c>,
    /// <c>equivalentDocument</c>…). The change of the enablement of a company (<c>company</c>) is not about a document.
    /// </remarks>
    public Task<ProviderDocumentEvent?> ReadAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        try
        {
            if (JsonNode.Parse(body ?? string.Empty) is not JsonObject root)
            {
                return Task.FromResult<ProviderDocumentEvent?>(null);
            }

            var document = root.Where(x => x.Key != "company" && x.Value is JsonObject inner && inner["id"] is not null).Select(x => (JsonObject)x.Value!).FirstOrDefault();
            var id = document?["id"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

            if (string.IsNullOrWhiteSpace(id))
            {
                return Task.FromResult<ProviderDocumentEvent?>(null);
            }

            var status = document!["legalStatus"] is JsonValue legal && legal.TryGetValue<string>(out var legalText) ? legalText : document["status"]?.ToString();

            return Task.FromResult<ProviderDocumentEvent?>(new ProviderDocumentEvent(id, status));
        }
        catch (JsonException)
        {
            return Task.FromResult<ProviderDocumentEvent?>(null);
        }
    }

    private async Task<AlanubeDocument?> FindAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(lookup.ProviderDocumentId))
        {
            var byId = await client.SendAsync(HttpMethod.Get, AlanubePaths.ByIdOf(lookup.Type, lookup.ProviderDocumentId), null, Token(), null, cancellationToken);

            if (byId.IsSuccess && AlanubeDocument.From(byId.Json()) is { } found)
            {
                return found;
            }
        }

        if (lookup.Number is null || string.IsNullOrWhiteSpace(issuer?.ProviderIssuerId))
        {
            return null;
        }

        return await FindByNumberAsync(issuer.ProviderIssuerId, lookup.Type, lookup.Number, cancellationToken);
    }

    private async Task<AlanubeDocument?> FindByNumberAsync(string companyId, ElectronicDocumentType type, DocumentNumber? number, CancellationToken cancellationToken)
    {
        var path = number is null ? null : AlanubePaths.ByNumberOf(type, companyId, number, options.Value.For(ElectronicDocumentType.PosTicket).DocumentType);

        if (path is null)
        {
            return null;
        }

        var answer = await client.SendAsync(HttpMethod.Get, path, null, Token(), null, cancellationToken);

        if (!answer.IsSuccess)
        {
            return null;
        }

        // A number may have several sends (a rejected one and its correction): the accepted one wins, then one in process.
        var sends = AlanubeDocument.ListFrom(answer.Json()).Where(x => x.Number is null || x.Number == number).ToList();

        return sends.FirstOrDefault(x => x.IsAccepted) ?? sends.FirstOrDefault(x => !x.IsRejected) ?? sends.FirstOrDefault();
    }

    private async Task<ProviderFile?> DownloadAsync(Uri? url, string name, CancellationToken cancellationToken)
    {
        if (url is null)
        {
            return null;
        }

        var content = await client.DownloadAsync(url, cancellationToken);

        return content is null || content.Length == 0 ? null : new ProviderFile(name, content);
    }

    private ProviderFile? Unzip(byte[]? package, string extension, string name)
    {
        if (package is null || package.Length == 0)
        {
            return null;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);

            var entry = archive.Entries.FirstOrDefault(x => x.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            return copy.Length == 0 ? null : new ProviderFile(name, copy.ToArray());
        }
        catch (InvalidDataException)
        {
            logger.LogWarning("The package of the files of {Name} is not a zip.", name);

            return null;
        }
    }

    private string Token()
    {
        var token = options.Value.Master.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.PlatformTokenNotConfigured.GetCode(), Errors.PlatformTokenNotConfigured.GetMessage());
        }

        return token;
    }

    private static string CompanyIdOf(ProviderIssuer issuer)
    {
        // An issuer that was never created in Alanube cannot issue there: it is the issuer, not the document, to fix.
        if (string.IsNullOrWhiteSpace(issuer.ProviderIssuerId))
        {
            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderIssuerNotReady.GetCode(), Errors.ProviderIssuerNotReady.GetMessage());
        }

        return issuer.ProviderIssuerId;
    }

    private static (string Id, string? GovernmentStatus)? CompanyFrom(JsonNode? root, string? identification)
    {
        var body = root as JsonObject;
        IEnumerable<JsonObject> companies = body?["company"] is JsonObject single ? [single] : (body?["companies"] as JsonArray ?? []).OfType<JsonObject>();

        // The main company may share the tax id in the sandbox (the one of Alegra): the associated one wins.
        var company = companies
            .Where(x => identification is null || string.Equals(x["identification"]?.ToString(), identification, StringComparison.Ordinal))
            .OrderBy(x => string.Equals(x["type"]?.ToString(), "associated", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        var id = company?["id"]?.ToString();

        return string.IsNullOrWhiteSpace(id) ? null : (id, company!["governmentStatus"]?.ToJsonString());
    }

    private static ProviderNumberingRange? RangeOf(JsonObject resolution)
    {
        var number = resolution["resolutionNumber"]?.ToString();
        var from = DateOf(resolution["startDate"]?.ToString());
        var until = DateOf(resolution["endDate"]?.ToString());

        if (string.IsNullOrWhiteSpace(number) || from is null || until is null)
        {
            return null;
        }

        return new ProviderNumberingRange(resolution["technicalKey"]?.ToString(), resolution["prefix"]?.ToString() ?? string.Empty, number, LongOf(resolution["minNumber"]), LongOf(resolution["maxNumber"]), from.Value, until.Value);
    }

    private static long LongOf(JsonNode? node) => long.TryParse(node?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static LocalDate? DateOf(string? value) => value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? LocalDate.FromDateOnly(date) : null;

    private static string AlanubeFallbackReason(AlanubeAnswer answer) => string.IsNullOrWhiteSpace(answer.Body) ? ((int)answer.Status).ToString(CultureInfo.InvariantCulture) : answer.Body.Length > 1000 ? answer.Body[..1000] : answer.Body;

    private static CodeDesignPlusException NotUnderstood() => new(Layer.Infrastructure, Errors.ProviderAnswerNotUnderstood.GetCode(), Errors.ProviderAnswerNotUnderstood.GetMessage());
}
