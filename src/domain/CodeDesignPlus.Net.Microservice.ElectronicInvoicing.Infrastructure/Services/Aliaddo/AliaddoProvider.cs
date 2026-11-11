using System.Globalization;
using System.Text.Json;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo.Models;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// The adapter of Aliaddo Nitro: the platform's account (the parent organization) operates every issuer, this service
/// gives the numbers, and the documents go with the <c>mode</c> of the environment of the issuer.
/// </summary>
/// <remarks>
/// <para>
/// Built only from the published documentation (<c>docs/aliaddo/</c>). The paths, the fields and the statuses are
/// those of the documentation; what it does not publish is marked <c>TODO(pendings/663)</c> to validate with
/// <c>mode = Test</c>.
/// </para>
/// <para>
/// <b>Amounts.</b> The fields the documentation types as <c>integer</c> (unit price, quantity, discount of the line,
/// totals) go in whole units of the currency: the minor units are divided by ten to the decimal digits of the currency
/// and rounded half away from zero. The fields typed as <c>number</c> (base and amount of each tax, rates) keep the
/// decimals of the currency. The rates go as percentages (1900 basis points are <c>19</c>).
/// </para>
/// <para>
/// <b>Statuses.</b> <c>Pendiente</c> and <c>Procesando</c> are processing; <c>NoDisponible</c> (the guides write
/// <c>DianNoDisponible</c>) is the tax authority not answering, which is not a rejection; <c>Aprobada</c> is validated
/// and <c>Rechazada</c> rejected. <c>Error</c> is kept as processing, so it keeps being asked about and stalls if it never
/// ends. Any other status is taken as processing too.
/// </para>
/// <para>
/// <b>Credentials.</b> The token is the one of the parent organization (<c>Aliaddo:Master:Token</c>, from the Vault).
/// An issuer that carries its own token in its sealed credentials (secret <c>Token</c>) uses it instead: that is how the
/// child organizations will work once Aliaddo says how their token is obtained.
/// </para>
/// </remarks>
/// <param name="client">The calls to the API.</param>
/// <param name="options">The options of Aliaddo.</param>
/// <param name="logger">The logger.</param>
public sealed class AliaddoProvider(IAliaddoClient client, IOptions<AliaddoOptions> options, ILogger<AliaddoProvider> logger) : IIssuerProvisioningProvider, IProviderEventReader
{
    /// <summary>
    /// The code of the adapter.
    /// </summary>
    public const string ProviderCode = "Aliaddo";

    /// <summary>
    /// The name of the secret of an issuer that carries its own token.
    /// </summary>
    public const string TokenSecret = "Token";

    /// <summary>
    /// The types of document Nitro issues. The adjustment note of a point of sale ticket is its credit adjustment note
    /// to the equivalent document (code 94, operation 20: <c>notas-de-ajuste-debito-y-credito-al-documento-equivalente.md</c>).
    /// </summary>
    public static readonly IReadOnlySet<ElectronicDocumentType> SupportedTypes = new HashSet<ElectronicDocumentType>([ElectronicDocumentType.Invoice, ElectronicDocumentType.CreditNote, ElectronicDocumentType.DebitNote, ElectronicDocumentType.PosTicket, ElectronicDocumentType.PosAdjustmentNote]);

    private const string ResolutionsPath = "/v2/resolutions/resolutions-dian";
    private const string ChildrenPath = "/companies/isv";

    // The statuses of docs/aliaddo (consultar-por-id and the guides of the contingency); a comparison by value, because
    // Nitro writes them in Spanish.
    private static readonly HashSet<string> Approved = new(["Aprobada"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Rejected = new(["Rechazada"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DianUnavailable = new(["NoDisponible", "DianNoDisponible"], StringComparer.OrdinalIgnoreCase);

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
        IdempotencyMode.LookupByNumber,
        ProvidesXml: true,
        DeliversToBuyer: true,
        RequiresProviderCatalog: false);

    /// <inheritdoc/>
    public async Task<CredentialsValidation> ValidateCredentialsAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        try
        {
            await client.SendAsync(HttpMethod.Get, ResolutionsPath, null, TokenOf(issuer), cancellationToken);

            return new CredentialsValidation(true, null);
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

        var problems = AliaddoMapper.Problems(document);

        // What Aliaddo would refuse is refused here, without spending a call or a document.
        if (problems.Count > 0)
        {
            return new ProviderSubmission(null, new ProviderDocumentState(ProviderDocumentStatus.Rejected, null, null, document.Number, null, problems, RejectionOrigin.Provider, null));
        }

        var request = AliaddoMapper.ToRequest(issuer, document, options.Value);

        var answer = await client.SendAsync(HttpMethod.Post, AliaddoPaths.CreateOf(document.Type), request, TokenOf(issuer), cancellationToken);

        if (answer.IsValidationRefusal || !IsSuccess(answer))
        {
            return new ProviderSubmission(null, new ProviderDocumentState(ProviderDocumentStatus.Rejected, null, null, document.Number, null, ReasonsOf(answer.Body), RejectionOrigin.Provider, ((int)answer.Status).ToString(CultureInfo.InvariantCulture)));
        }

        var response = ReadOrFail<AliaddoSubmissionResponse>(answer);

        var state = new ProviderDocumentState(StatusOf(response.Status), response.Id, response.Cufe, document.Number, null, response.StatusReasons ?? [], RejectionOrigin.Dian, response.Status);

        return new ProviderSubmission(response.Id, state);
    }

    /// <inheritdoc/>
    public async Task<ProviderDocumentState?> GetStateAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        var found = await FindAsync(issuer, lookup, cancellationToken);

        if (found is null)
        {
            return null;
        }

        var number = found.Prefix is not null && found.Consecutive is { } consecutive ? new DocumentNumber(found.Prefix, consecutive) : lookup.Number;
        var reasons = (found.StatusReasons ?? []).Concat(string.IsNullOrWhiteSpace(found.RejectReason) ? [] : [found.RejectReason]).ToList();

        return new ProviderDocumentState(StatusOf(found.Status), found.Id ?? lookup.ProviderDocumentId, found.Cuv, number, DateOf(found.IssuedAt), reasons, RejectionOrigin.Dian, found.Status);
    }

    /// <inheritdoc/>
    public async Task<ProviderFiles> GetFilesAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        var found = await FindAsync(issuer, lookup, cancellationToken);

        if (found is null)
        {
            return ProviderFiles.None;
        }

        var token = TokenOf(issuer);
        var name = found.Prefix is not null && found.Consecutive is { } consecutive ? new DocumentNumber(found.Prefix, consecutive).ToString() : lookup.Number?.ToString() ?? found.Id ?? "document";

        var xml = await DownloadAsync(found.UrlXmlDocument, $"{name}.xml", token, cancellationToken);
        var attached = await DownloadAsync(found.UrlXmlAttachedDocument, $"{name}-attached.xml", token, cancellationToken);
        var pdf = await DownloadAsync(found.UrlPdf, $"{name}.pdf", token, cancellationToken);

        return new ProviderFiles(xml, attached, pdf);
    }

    /// <inheritdoc/>
    public async Task<ProvisionedIssuer> ProvisionAsync(ProviderIssuer issuer, IssuerContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(contact);

        var parts = issuer.TaxId.Split('-');

        var request = new AliaddoChildRequest
        {
            Company = new AliaddoChildCompany
            {
                IdentificationTypeId = issuer.TaxIdTypeCode,
                IdentificationNumber = parts[0],
                IdentificationCheck = parts.Length > 1 ? parts[1] : string.Empty,
                Email = issuer.Email,
                Phone = issuer.Phone,
                Name = issuer.LegalName
            },
            User = new AliaddoChildUser
            {
                FirstName = contact.FirstName,
                LastName = contact.LastName,
                Email = contact.Email,
                PhoneNumber = contact.Phone
            }
        };

        // The child is created with the token of the parent organization, always.
        var answer = await client.SendAsync(HttpMethod.Post, ChildrenPath, request, MasterToken(), cancellationToken);
        var child = IsSuccess(answer) ? answer.Read<AliaddoChildResponse>() : null;

        if (child is null || string.IsNullOrWhiteSpace(child.Id))
        {
            logger.LogWarning("Aliaddo did not create the child organization of tenant {Tenant}: {Status}", issuer.Tenant, (int)answer.Status);

            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderAnswerNotUnderstood.GetCode(), Errors.ProviderAnswerNotUnderstood.GetMessage());
        }

        // TODO(pendings/663): the child is born with contractSignatureStatus "pending" and enabled false; it cannot issue
        // until Aliaddo approves the signature, and how the parent issues on its behalf is not documented.
        return new ProvisionedIssuer(child.Id, child.ContractSignatureStatus);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderNumberingRange>> GetNumberingRangesAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        var answer = await client.SendAsync(HttpMethod.Get, ResolutionsPath, null, TokenOf(issuer), cancellationToken);

        if (!IsSuccess(answer) || string.IsNullOrWhiteSpace(answer.Body))
        {
            return [];
        }

        // TODO(pendings/663): the documentation shows a single object; a list is read too, in case it is one per resolution.
        var resolutions = answer.Body.TrimStart().StartsWith('[') ? answer.Read<List<AliaddoResolutionResponse>>() ?? [] : [answer.Read<AliaddoResolutionResponse>()!];

        return [.. resolutions.Where(x => x is not null && x.Prefix is not null).Select(x => new ProviderNumberingRange(x.Key, x.Prefix!, x.Number.ToString(CultureInfo.InvariantCulture), x.RangeInitial, x.RangeFinal, DateOf(x.ValidFrom) ?? default, DateOf(x.ValidUntil) ?? default))];
    }

    /// <inheritdoc/>
    public bool IsAuthentic(string body, IReadOnlyDictionary<string, string> headers)
    {
        var webhooks = options.Value.Webhooks;

        if (!webhooks.Enabled || headers is null)
        {
            return false;
        }

        var signature = headers.FirstOrDefault(x => string.Equals(x.Key, webhooks.SignatureHeader, StringComparison.OrdinalIgnoreCase)).Value;

        return AliaddoSignature.Matches(body, signature, webhooks);
    }

    /// <inheritdoc/>
    public Task<ProviderDocumentEvent?> ReadAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        var webhooks = options.Value.Webhooks;

        try
        {
            using var json = JsonDocument.Parse(body);

            var id = PropertyOf(json.RootElement, webhooks.DocumentIdProperty);

            return Task.FromResult(string.IsNullOrWhiteSpace(id) ? null : new ProviderDocumentEvent(id, PropertyOf(json.RootElement, webhooks.StatusProperty)));
        }
        catch (JsonException)
        {
            return Task.FromResult<ProviderDocumentEvent?>(null);
        }
    }

    private async Task<AliaddoDocumentResponse?> FindAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var token = TokenOf(issuer);

        if (!string.IsNullOrWhiteSpace(lookup.ProviderDocumentId))
        {
            var byId = await client.SendAsync(HttpMethod.Get, AliaddoPaths.ByIdOf(lookup.Type, lookup.ProviderDocumentId), null, token, cancellationToken);

            if (IsSuccess(byId) && byId.Read<AliaddoDocumentResponse>() is { Id: not null } found)
            {
                return found;
            }
        }

        if (lookup.Number is null)
        {
            return null;
        }

        // TODO(pendings/663): it is not documented whether this finds a send whose answer was lost; to validate with mode = Test.
        var byNumber = await client.SendAsync(HttpMethod.Get, AliaddoPaths.ByNumberOf(lookup.Type, lookup.Number), null, token, cancellationToken);

        return IsSuccess(byNumber) ? byNumber.Read<AliaddoDocumentResponse>() is { Id: not null } numbered ? numbered : null : null;
    }

    private async Task<ProviderFile?> DownloadAsync(string? url, string name, string token, CancellationToken cancellationToken)
    {
        var resolved = UrlOf(url);

        if (resolved is null)
        {
            return null;
        }

        var content = await client.DownloadAsync(resolved, token, cancellationToken);

        return content is null || content.Length == 0 ? null : new ProviderFile(name, content);
    }

    private Uri? UrlOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http")
        {
            return absolute;
        }

        // TODO(pendings/663): the documentation shows UrlPdf as a path without host (1394/transactions/…); until it is
        // known where it points, without Aliaddo:FilesBaseUrl the files stay in the provider.
        return options.Value.FilesBaseUrl is { } baseUrl ? new Uri(baseUrl, url.TrimStart('/')) : null;
    }

    private string TokenOf(ProviderIssuer issuer)
    {
        if (issuer?.Secrets is not null && issuer.Secrets.TryGetValue(TokenSecret, out var own) && !string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        return MasterToken();
    }

    private string MasterToken()
    {
        var token = options.Value.Master.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.PlatformTokenNotConfigured.GetCode(), Errors.PlatformTokenNotConfigured.GetMessage());
        }

        return token;
    }

    /// <summary>
    /// The status of Aliaddo, normalized.
    /// </summary>
    /// <param name="status">The status as Aliaddo wrote it.</param>
    /// <returns>The normalized status.</returns>
    public static ProviderDocumentStatus StatusOf(string? status)
    {
        if (status is null)
        {
            return ProviderDocumentStatus.Processing;
        }

        if (Approved.Contains(status))
        {
            return ProviderDocumentStatus.Accepted;
        }

        if (Rejected.Contains(status))
        {
            return ProviderDocumentStatus.Rejected;
        }

        // TODO(pendings/663): "Error" is kept as processing (it keeps being asked about and stalls if it never ends) until
        // a test in mode = Test shows whether it is final.
        return DianUnavailable.Contains(status) ? ProviderDocumentStatus.DianUnavailable : ProviderDocumentStatus.Processing;
    }

    private static bool IsSuccess(AliaddoAnswer answer) => (int)answer.Status is >= 200 and < 300;

    private static T ReadOrFail<T>(AliaddoAnswer answer)
    {
        try
        {
            var read = answer.Read<T>();

            if (read is not null)
            {
                return read;
            }
        }
        catch (JsonException)
        {
            // Falls to the error below.
        }

        throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderAnswerNotUnderstood.GetCode(), Errors.ProviderAnswerNotUnderstood.GetMessage());
    }

    /// <summary>
    /// The reasons of a refusal of Aliaddo, as they came. The documentation does not publish the shape of the error, so
    /// the usual fields are read and, if none is there, the text itself.
    /// </summary>
    /// <param name="body">The body of the refusal.</param>
    /// <returns>The reasons.</returns>
    public static List<string> ReasonsOf(string body)
    {
        const int MaxLength = 1000;

        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var reasons = new List<string>();

            Collect(json.RootElement, reasons);

            if (reasons.Count > 0)
            {
                return reasons;
            }
        }
        catch (JsonException)
        {
            // Not JSON: the text itself is the reason.
        }

        return [body.Length > MaxLength ? body[..MaxLength] : body];
    }

    private static void Collect(JsonElement element, List<string> reasons)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            reasons.Add(element.GetString()!);

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, reasons);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("message", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("errors", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("StatusReasons", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("detail", StringComparison.OrdinalIgnoreCase))
            {
                Collect(property.Value, reasons);
            }
        }
    }

    private static string? PropertyOf(JsonElement root, string? property)
    {
        if (string.IsNullOrWhiteSpace(property) || root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var each in root.EnumerateObject())
        {
            if (string.Equals(each.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                return each.Value.ValueKind == JsonValueKind.String ? each.Value.GetString() : each.Value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// A date of Aliaddo (<c>2026-02-05T00:00:00</c> or <c>2026-02-05</c>) as a calendar date.
    /// </summary>
    /// <param name="value">The date as Aliaddo wrote it.</param>
    /// <returns>The calendar date, or null when it is not a date.</returns>
    public static LocalDate? DateOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? LocalDate.FromDateOnly(date) : null;
    }
}
