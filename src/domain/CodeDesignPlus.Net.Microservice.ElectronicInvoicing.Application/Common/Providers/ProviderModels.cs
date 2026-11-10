namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

/// <summary>
/// The issuer as an adapter needs it, with its credentials already opened. Lives in memory for one operation and is
/// never logged.
/// </summary>
/// <param name="Tenant">The tenant of the issuer.</param>
/// <param name="IssuerId">The id of the issuer.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country.</param>
/// <param name="TaxIdTypeCode">Code of the type of identification.</param>
/// <param name="TaxId">Tax id with its check digit after a hyphen.</param>
/// <param name="LegalName">Legal name.</param>
/// <param name="PersonTypeCode">Code of the type of person.</param>
/// <param name="TaxRegimeCode">Code of the tax regime.</param>
/// <param name="TaxResponsibilities">Codes of the fiscal responsibilities.</param>
/// <param name="Address">The address.</param>
/// <param name="Email">The email.</param>
/// <param name="Phone">The phone.</param>
/// <param name="TimeZoneId">IANA time zone of the issuer.</param>
/// <param name="ProviderIssuerId">Id of the issuer in the provider.</param>
/// <param name="Environment">Where its documents go.</param>
/// <param name="TestSetId">The test set of the enablement tests.</param>
/// <param name="Settings">What the adapter needs per issuer that is not secret.</param>
/// <param name="Secrets">The opened credentials, when the issuer brings its own.</param>
public sealed record ProviderIssuer(
    Guid Tenant,
    Guid IssuerId,
    string Country,
    string TaxIdTypeCode,
    string TaxId,
    string LegalName,
    string PersonTypeCode,
    string TaxRegimeCode,
    IReadOnlyList<string> TaxResponsibilities,
    PostalAddress Address,
    string Email,
    string Phone,
    string TimeZoneId,
    string? ProviderIssuerId,
    IssuerEnvironment Environment,
    string? TestSetId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets)
{
    /// <summary>
    /// The issuer as an adapter needs it.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="secrets">Its opened credentials.</param>
    /// <returns>The issuer for the adapter.</returns>
    public static ProviderIssuer From(IssuerAggregate issuer, IReadOnlyDictionary<string, string> secrets) => new(
        issuer.Tenant,
        issuer.Id,
        issuer.Country,
        issuer.TaxIdTypeCode,
        issuer.TaxId,
        issuer.LegalName,
        issuer.PersonTypeCode,
        issuer.TaxRegimeCode,
        issuer.TaxResponsibilities,
        issuer.Address,
        issuer.Email,
        issuer.Phone,
        issuer.TimeZoneId,
        issuer.ProviderIssuerId,
        issuer.Environment,
        issuer.TestSetId,
        new Dictionary<string, string>(issuer.ProviderSettings ?? []),
        secrets);

    /// <summary>
    /// Never prints a secret.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(ProviderIssuer)} {{ issuer: {IssuerId}, environment: {Environment}, secrets: {Secrets.Count} }}";
}

/// <summary>
/// A document as the adapter sends it: neutral content in minor units and the tax authority's codes.
/// </summary>
/// <param name="DocumentId">The id of the document in this service.</param>
/// <param name="Type">The type of the document.</param>
/// <param name="Number">The number, when this service numbers.</param>
/// <param name="NumberingRange">The resolution of the number, when this service numbers.</param>
/// <param name="Content">What the document says.</param>
/// <param name="IdempotencyKey">The same key on every send of the same document.</param>
/// <param name="IssueDate">The day it is sent in the time zone of the issuer, which is the issue date the provider puts: the due
/// date of a cash sale, and the earliest one of any document (<see cref="DocumentContent.DueDateOn"/>).</param>
public sealed record ProviderDocument(Guid DocumentId, ElectronicDocumentType Type, DocumentNumber? Number, ProviderNumberingRange? NumberingRange, DocumentContent Content, string IdempotencyKey, LocalDate IssueDate)
{
    /// <summary>
    /// How many times the content was corrected after a rejection: a provider that dedupes the sends by a key takes a
    /// correction as a new send even when the corrected content is the same.
    /// </summary>
    public int Revision { get; init; }
}

/// <summary>
/// A numbering resolution as a provider has it.
/// </summary>
/// <param name="TechnicalKey">The technical key.</param>
/// <param name="Prefix">The prefix.</param>
/// <param name="ResolutionNumber">The number of the resolution.</param>
/// <param name="From">The first number.</param>
/// <param name="To">The last number.</param>
/// <param name="ValidFrom">The first day of the validity.</param>
/// <param name="ValidUntil">The last day of the validity.</param>
public sealed record ProviderNumberingRange(string? TechnicalKey, string Prefix, string ResolutionNumber, long From, long To, LocalDate ValidFrom, LocalDate ValidUntil);

/// <summary>
/// What a provider answered when it received a document.
/// </summary>
/// <param name="ProviderDocumentId">The id the provider gave the document, when it kept it.</param>
/// <param name="State">How the document stands.</param>
public sealed record ProviderSubmission(string? ProviderDocumentId, ProviderDocumentState State);

/// <summary>
/// How a document stands in a provider.
/// </summary>
/// <param name="Status">The status, normalized.</param>
/// <param name="ProviderDocumentId">The id in the provider.</param>
/// <param name="Cufe">The CUFE, once validated.</param>
/// <param name="Number">The number, when the provider numbers.</param>
/// <param name="IssueDate">The issue date the provider put.</param>
/// <param name="Reasons">The reasons the provider gave, as they came.</param>
/// <param name="RejectionOrigin">Who rejected it, when it was rejected.</param>
/// <param name="Raw">The status as the provider wrote it, without secrets.</param>
public sealed record ProviderDocumentState(ProviderDocumentStatus Status, string? ProviderDocumentId, string? Cufe, DocumentNumber? Number, LocalDate? IssueDate, IReadOnlyList<string> Reasons, RejectionOrigin? RejectionOrigin, string? Raw);

/// <summary>
/// How to find a document in a provider: by its id there, by its number (a send whose answer was lost) or by the
/// idempotency key.
/// </summary>
/// <param name="Type">The type of the document.</param>
/// <param name="ProviderDocumentId">The id in the provider.</param>
/// <param name="Number">The number.</param>
/// <param name="IdempotencyKey">The idempotency key of the sends.</param>
public sealed record ProviderDocumentLookup(ElectronicDocumentType Type, string? ProviderDocumentId, DocumentNumber? Number, string IdempotencyKey);

/// <summary>
/// A file handed over by a provider.
/// </summary>
/// <param name="Name">The name, with its extension.</param>
/// <param name="Content">The bytes.</param>
public sealed record ProviderFile(string Name, byte[] Content);

/// <summary>
/// The files of a document as a provider hands them over.
/// </summary>
/// <param name="Xml">The signed XML.</param>
/// <param name="AttachedDocument">The attached document of the tax authority.</param>
/// <param name="Pdf">The graphic representation.</param>
public sealed record ProviderFiles(ProviderFile? Xml, ProviderFile? AttachedDocument, ProviderFile? Pdf)
{
    /// <summary>
    /// No files.
    /// </summary>
    public static ProviderFiles None { get; } = new(null, null, null);
}

/// <summary>
/// An issuer as the provider created it.
/// </summary>
/// <param name="ProviderIssuerId">Its id in the provider.</param>
/// <param name="Raw">Its status as the provider wrote it, without secrets.</param>
public sealed record ProvisionedIssuer(string ProviderIssuerId, string? Raw);

/// <summary>
/// The person a provider registers as the user of an issuer.
/// </summary>
/// <param name="FirstName">First names.</param>
/// <param name="LastName">Last names.</param>
/// <param name="Email">Email.</param>
/// <param name="Phone">Phone.</param>
public sealed record IssuerContact(string FirstName, string LastName, string Email, string Phone);

/// <summary>
/// An option of the account of an issuer in its provider.
/// </summary>
/// <param name="Id">The id in the provider.</param>
/// <param name="Name">The name.</param>
public sealed record ProviderSetupOption(string Id, string Name);

/// <summary>
/// The options of the account of an issuer in its provider, by kind.
/// </summary>
/// <param name="Options">The options, by kind.</param>
public sealed record ProviderSetupOptions(IReadOnlyDictionary<string, IReadOnlyList<ProviderSetupOption>> Options);

/// <summary>
/// What a provider said when it called this service.
/// </summary>
/// <param name="ProviderDocumentId">The document it is about.</param>
/// <param name="Status">The status it reported, as it came; only a hint, the state is asked again.</param>
public sealed record ProviderDocumentEvent(string ProviderDocumentId, string? Status);

/// <summary>
/// How a test set of the tax authority stands in a provider that sends it on its own.
/// </summary>
/// <param name="Status">The status, normalized.</param>
/// <param name="Raw">The status as the provider wrote it.</param>
public sealed record ProviderTestSet(ProviderDocumentStatus Status, string? Raw);

/// <summary>
/// Whether a provider accepted the credentials of an issuer.
/// </summary>
/// <param name="Accepted">Whether they were accepted.</param>
/// <param name="Reason">Why not, as the provider said it.</param>
public sealed record CredentialsValidation(bool Accepted, string? Reason);
