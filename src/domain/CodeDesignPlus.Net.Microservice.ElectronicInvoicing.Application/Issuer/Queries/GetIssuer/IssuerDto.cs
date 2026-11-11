using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;

/// <summary>
/// What the service shows of an issuer. It never carries a credential: only the last four characters of each.
/// </summary>
public class IssuerDto : IDtoBase
{
    /// <summary>
    /// The id of the issuer.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// The tenant of the issuer. For the platform's issuer it is the tenant reserved for it (see <c>PlatformScope</c>).
    /// </summary>
    public Guid Tenant { get; set; }

    /// <summary>
    /// Whether it belongs to the platform's issuer and not to a tenant.
    /// </summary>
    public bool PlatformIssuer { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country.
    /// </summary>
    public string Country { get; set; } = null!;

    /// <summary>
    /// Code of the type of identification.
    /// </summary>
    public string TaxIdTypeCode { get; set; } = null!;

    /// <summary>
    /// Tax id with its check digit after a hyphen.
    /// </summary>
    public string TaxId { get; set; } = null!;

    /// <summary>
    /// Legal name.
    /// </summary>
    public string LegalName { get; set; } = null!;

    /// <summary>
    /// Code of the type of person.
    /// </summary>
    public string PersonTypeCode { get; set; } = null!;

    /// <summary>
    /// Code of the tax regime.
    /// </summary>
    public string TaxRegimeCode { get; set; } = null!;

    /// <summary>
    /// Codes of the fiscal responsibilities.
    /// </summary>
    public List<string> TaxResponsibilities { get; set; } = [];

    /// <summary>
    /// The address.
    /// </summary>
    public PostalAddress Address { get; set; } = null!;

    /// <summary>
    /// The email.
    /// </summary>
    public string Email { get; set; } = null!;

    /// <summary>
    /// The phone.
    /// </summary>
    public string Phone { get; set; } = null!;

    /// <summary>
    /// IANA time zone of the issuer.
    /// </summary>
    public string TimeZoneId { get; set; } = null!;

    /// <summary>
    /// The provider it issues with.
    /// </summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The providers the country of the issuer allows, the default first.
    /// </summary>
    public List<string> AvailableProviders { get; set; } = [];

    /// <summary>
    /// Whether it exists in the provider.
    /// </summary>
    public bool Provisioned { get; set; }

    /// <summary>
    /// The enablement.
    /// </summary>
    public IssuerStatus Status { get; set; }

    /// <summary>
    /// Where its documents go.
    /// </summary>
    public IssuerEnvironment Environment { get; set; }

    /// <summary>
    /// The test set of the enablement tests.
    /// </summary>
    public string? TestSetId { get; set; }

    /// <summary>
    /// The version of the mandate accepted, if any.
    /// </summary>
    public string? MandateVersion { get; set; }

    /// <summary>
    /// When the mandate was accepted.
    /// </summary>
    public Instant? MandateAcceptedAt { get; set; }

    /// <summary>
    /// Who accepted the mandate.
    /// </summary>
    public Guid? MandateAcceptedBy { get; set; }

    /// <summary>
    /// The signed mandate in ms-filestorage.
    /// </summary>
    public StoredFile? MandateFile { get; set; }

    /// <summary>
    /// The last four characters of each connected credential, by name.
    /// </summary>
    public Dictionary<string, string> CredentialHints { get; set; } = [];

    /// <summary>
    /// Code of the error catalog that says why it is suspended, while it is.
    /// </summary>
    public string? SuspensionReasonCode { get; set; }

    /// <summary>
    /// Whether it can send documents now.
    /// </summary>
    public bool CanIssue { get; set; }

    /// <summary>
    /// What its provider can do; null when no adapter serves its provider.
    /// </summary>
    public CapabilitiesDto? Capabilities { get; set; }

    /// <summary>
    /// Its quota of documents.
    /// </summary>
    public QuotaDto Quota { get; set; } = null!;

    /// <summary>
    /// The issuer as the service shows it.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="capabilities">What its provider can do.</param>
    /// <param name="quota">Its quota, if it has one.</param>
    /// <param name="availableProviders">The providers its country allows.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The DTO.</returns>
    public static IssuerDto From(IssuerAggregate issuer, ProviderCapabilities? capabilities, DocumentQuotaAggregate? quota, IReadOnlyList<string> availableProviders, Instant now) => new()
    {
        Id = issuer.Id,
        Tenant = issuer.Tenant,
        PlatformIssuer = PlatformScope.Owns(issuer.Tenant),
        Country = issuer.Country,
        TaxIdTypeCode = issuer.TaxIdTypeCode,
        TaxId = issuer.TaxId,
        LegalName = issuer.LegalName,
        PersonTypeCode = issuer.PersonTypeCode,
        TaxRegimeCode = issuer.TaxRegimeCode,
        TaxResponsibilities = [.. issuer.TaxResponsibilities],
        Address = issuer.Address,
        Email = issuer.Email,
        Phone = issuer.Phone,
        TimeZoneId = issuer.TimeZoneId,
        Provider = issuer.Provider,
        AvailableProviders = [.. availableProviders],
        Provisioned = !string.IsNullOrWhiteSpace(issuer.ProviderIssuerId),
        Status = issuer.Status,
        Environment = issuer.Environment,
        TestSetId = issuer.TestSetId,
        MandateVersion = issuer.Mandate?.Version,
        MandateAcceptedAt = issuer.Mandate?.AcceptedAt,
        MandateAcceptedBy = issuer.Mandate?.AcceptedBy,
        MandateFile = issuer.Mandate?.File,
        CredentialHints = new Dictionary<string, string>(issuer.Credentials?.Hints ?? []),
        SuspensionReasonCode = issuer.SuspensionReasonCode,
        CanIssue = issuer.CanIssue && capabilities is not null,
        Capabilities = capabilities is null ? null : CapabilitiesDto.From(capabilities),
        Quota = QuotaDto.From(quota, now)
    };
}

/// <summary>
/// What the provider of an issuer can do, as the screens need it.
/// </summary>
public class CapabilitiesDto
{
    /// <summary>
    /// The types of document it issues.
    /// </summary>
    public List<ElectronicDocumentType> DocumentTypes { get; set; } = [];

    /// <summary>
    /// Whether the issuers are created through the API or by hand.
    /// </summary>
    public IssuerProvisioningMode IssuerProvisioning { get; set; }

    /// <summary>
    /// Whether it works with the platform's account or each issuer brings its credentials.
    /// </summary>
    public CredentialScope CredentialScope { get; set; }

    /// <summary>
    /// Who gives the numbers.
    /// </summary>
    public NumberingMode Numbering { get; set; }

    /// <summary>
    /// Whether the test set is sent through the API.
    /// </summary>
    public bool HabilitationByApi { get; set; }

    /// <summary>
    /// Whether an invoice can carry prepayments.
    /// </summary>
    public bool SupportsPrepayments { get; set; }

    /// <summary>
    /// Whether a note can correct a document issued elsewhere.
    /// </summary>
    public bool ExternalBillingReference { get; set; }

    /// <summary>
    /// How the result arrives.
    /// </summary>
    public StatusUpdatesMode StatusUpdates { get; set; }

    /// <summary>
    /// Whether the issue date sent is the one the document carries; when false the provider puts the day it receives it,
    /// and the caller adopts the date of the validated document.
    /// </summary>
    public bool IssuerSetsIssueDate { get; set; }

    /// <summary>
    /// The capabilities as the screens need them.
    /// </summary>
    /// <param name="capabilities">The capabilities.</param>
    /// <returns>The DTO.</returns>
    public static CapabilitiesDto From(ProviderCapabilities capabilities) => new()
    {
        DocumentTypes = [.. capabilities.DocumentTypes.Order()],
        IssuerProvisioning = capabilities.IssuerProvisioning,
        CredentialScope = capabilities.CredentialScope,
        Numbering = capabilities.Numbering,
        HabilitationByApi = capabilities.HabilitationByApi,
        SupportsPrepayments = capabilities.SupportsPrepayments,
        ExternalBillingReference = capabilities.ExternalBillingReference,
        StatusUpdates = capabilities.StatusUpdates,
        IssuerSetsIssueDate = capabilities.IssuerSetsIssueDate
    };
}

/// <summary>
/// The quota of documents of an issuer as it stands now.
/// </summary>
public class QuotaDto
{
    /// <summary>
    /// Documents allowed in the period.
    /// </summary>
    public long Limit { get; set; }

    /// <summary>
    /// Documents used in the period.
    /// </summary>
    public long Used { get; set; }

    /// <summary>
    /// Documents an invoice or a ticket can still use now; the notes always go.
    /// </summary>
    public long Remaining { get; set; }

    /// <summary>
    /// When the period started.
    /// </summary>
    public Instant? PeriodStart { get; set; }

    /// <summary>
    /// When the period ends.
    /// </summary>
    public Instant? PeriodEnd { get; set; }

    /// <summary>
    /// The quota as it stands now.
    /// </summary>
    /// <param name="quota">The quota, if the issuer has one.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The DTO; all zero without a quota.</returns>
    public static QuotaDto From(DocumentQuotaAggregate? quota, Instant now)
    {
        if (quota is null)
        {
            return new QuotaDto();
        }

        var remaining = quota.RemainingAt(now);
        var renewed = quota.PeriodEnd is { } end && now >= end && quota.RenewedUntil > end;

        return new QuotaDto
        {
            Limit = quota.Limit,
            Used = renewed ? 0 : quota.Used,
            Remaining = remaining,
            PeriodStart = renewed ? quota.PeriodEnd : quota.PeriodStart,
            PeriodEnd = quota.PeriodEndAt(now)
        };
    }
}
