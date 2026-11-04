namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

/// <summary>
/// What a provider can do and how, so the flow decides by capability and never by the name of the provider.
/// </summary>
/// <param name="IssuerProvisioning">Whether the issuers are created through the API or by hand in the provider.</param>
/// <param name="CredentialScope">Whether the provider works with the platform's account or each issuer brings its credentials.</param>
/// <param name="Numbering">Whether this service or the provider gives the numbers.</param>
/// <param name="HabilitationByApi">Whether the test set of the tax authority is sent through the API.</param>
/// <param name="DocumentTypes">The types of document the provider issues.</param>
/// <param name="SupportsPrepayments">Whether an invoice can carry prepayments.</param>
/// <param name="ExternalBillingReference">Whether a note can correct a document that was not issued through the provider.</param>
/// <param name="StatusUpdates">Whether the result is asked for or the provider calls with it.</param>
/// <param name="Idempotency">How a retry avoids issuing twice.</param>
/// <param name="ProvidesXml">Whether the provider hands over the XML.</param>
/// <param name="DeliversToBuyer">Whether the provider sends the document to the buyer.</param>
/// <param name="RequiresProviderCatalog">Whether the buyer and the products must exist in the provider first.</param>
public sealed record ProviderCapabilities(
    IssuerProvisioningMode IssuerProvisioning,
    CredentialScope CredentialScope,
    NumberingMode Numbering,
    bool HabilitationByApi,
    IReadOnlySet<ElectronicDocumentType> DocumentTypes,
    bool SupportsPrepayments,
    bool ExternalBillingReference,
    StatusUpdatesMode StatusUpdates,
    IdempotencyMode Idempotency,
    bool ProvidesXml,
    bool DeliversToBuyer,
    bool RequiresProviderCatalog);

/// <summary>
/// How a provider gets to know an issuer.
/// </summary>
public enum IssuerProvisioningMode
{
    /// <summary>
    /// This service creates the issuer through the API.
    /// </summary>
    Api = 1,

    /// <summary>
    /// The issuer already exists in the provider and brings its credentials.
    /// </summary>
    Manual = 2
}

/// <summary>
/// Whose credentials the provider works with.
/// </summary>
public enum CredentialScope
{
    /// <summary>
    /// The platform's account, which operates every issuer.
    /// </summary>
    MasterAccount = 1,

    /// <summary>
    /// Each issuer brings its own credentials, sealed in the issuer.
    /// </summary>
    PerIssuer = 2
}

/// <summary>
/// Who gives the numbers of the documents.
/// </summary>
public enum NumberingMode
{
    /// <summary>
    /// This service reserves the number when the document is created.
    /// </summary>
    ByPlatform = 1,

    /// <summary>
    /// The provider gives the number when it receives the document.
    /// </summary>
    ByProvider = 2
}

/// <summary>
/// How the result of a document arrives.
/// </summary>
public enum StatusUpdatesMode
{
    /// <summary>
    /// The status job asks for it.
    /// </summary>
    Poll = 1,

    /// <summary>
    /// The provider calls this service; the job stays as a safety net.
    /// </summary>
    Push = 2
}

/// <summary>
/// How a retry avoids issuing a document twice in the provider.
/// </summary>
public enum IdempotencyMode
{
    /// <summary>
    /// Before sending again, the document is looked up by its number and adopted if it is there.
    /// </summary>
    LookupByNumber = 1,

    /// <summary>
    /// Every send carries the same idempotency key.
    /// </summary>
    Header = 2
}

/// <summary>
/// The status of a document in a provider, normalized.
/// </summary>
public enum ProviderDocumentStatus
{
    /// <summary>
    /// Received and without a final answer yet.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// The tax authority does not answer; the provider keeps it in contingency. It is not a rejection.
    /// </summary>
    DianUnavailable = 2,

    /// <summary>
    /// Validated by the tax authority.
    /// </summary>
    Accepted = 3,

    /// <summary>
    /// Rejected by the provider or by the tax authority.
    /// </summary>
    Rejected = 4
}
