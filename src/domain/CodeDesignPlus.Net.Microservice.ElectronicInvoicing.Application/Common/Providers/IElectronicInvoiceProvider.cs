namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

/// <summary>
/// What every technology provider does: receive a document, say how it stands and hand over its files. Adding a
/// provider is registering its adapter; nothing else changes.
/// </summary>
/// <remarks>
/// The capabilities a provider may lack are separate interfaces (<see cref="IIssuerProvisioningProvider"/>,
/// <see cref="IProviderSetupProvider"/>, <see cref="IProviderEventReader"/>), and what differs between providers is said
/// by <see cref="Capabilities"/>, so nobody asks for a provider by its name.
/// </remarks>
public interface IElectronicInvoiceProvider
{
    /// <summary>
    /// The code of the adapter, the one the issuers keep and the routing names.
    /// </summary>
    string Code { get; }

    /// <summary>
    /// What this provider can do and how.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Whether the provider accepts the credentials the issuer works with.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether they were accepted.</returns>
    Task<CredentialsValidation> ValidateCredentialsAsync(ProviderIssuer issuer, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a document. A refusal of the provider's validation is an answer with <see cref="ProviderDocumentStatus.Rejected"/>,
    /// not an exception.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="document">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the provider answered.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>301</c> when the provider does not answer, or <c>302</c> when it rejects the credentials.</exception>
    Task<ProviderSubmission> SubmitAsync(ProviderIssuer issuer, ProviderDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// How a document stands in the provider.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="lookup">How to find the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state, or null when the provider does not have the document.</returns>
    Task<ProviderDocumentState?> GetStateAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken);

    /// <summary>
    /// The files of a validated document.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="lookup">How to find the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The files the provider could hand over; any of them may be missing.</returns>
    Task<ProviderFiles> GetFilesAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken);
}

/// <summary>
/// A provider that creates the issuers through its API and keeps their numbering resolutions.
/// </summary>
public interface IIssuerProvisioningProvider : IElectronicInvoiceProvider
{
    /// <summary>
    /// Creates the issuer in the provider.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="contact">The person the provider registers as its user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issuer as the provider created it.</returns>
    Task<ProvisionedIssuer> ProvisionAsync(ProviderIssuer issuer, IssuerContact contact, CancellationToken cancellationToken);

    /// <summary>
    /// The numbering resolutions the provider has for the issuer.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolutions.</returns>
    Task<IReadOnlyList<ProviderNumberingRange>> GetNumberingRangesAsync(ProviderIssuer issuer, CancellationToken cancellationToken);
}

/// <summary>
/// A provider whose issuers have to choose, among what their account has, how the documents are made (document types,
/// payment forms, sellers, taxes).
/// </summary>
public interface IProviderSetupProvider : IElectronicInvoiceProvider
{
    /// <summary>
    /// The options the account of the issuer has.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The options, by kind.</returns>
    Task<ProviderSetupOptions> GetSetupOptionsAsync(ProviderIssuer issuer, CancellationToken cancellationToken);
}

/// <summary>
/// A provider that tells the result of a document by calling this service.
/// </summary>
public interface IProviderEventReader : IElectronicInvoiceProvider
{
    /// <summary>
    /// Whether the call comes from the provider: its signature matches the body.
    /// </summary>
    /// <param name="body">The body, as it came.</param>
    /// <param name="headers">The headers.</param>
    /// <returns>Whether it can be trusted.</returns>
    bool IsAuthentic(string body, IReadOnlyDictionary<string, string> headers);

    /// <summary>
    /// The document the call is about.
    /// </summary>
    /// <param name="body">The body, as it came.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The event, or null when it is not about a document.</returns>
    Task<ProviderDocumentEvent?> ReadAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

/// <summary>
/// Finds the adapter of a provider by its code.
/// </summary>
public interface IElectronicInvoiceProviderFactory
{
    /// <summary>
    /// The adapter of a provider.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>203</c> when no adapter serves the provider.</exception>
    IElectronicInvoiceProvider Get(string provider);

    /// <summary>
    /// The adapter of a provider, when one serves it.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <returns>The adapter, or null.</returns>
    IElectronicInvoiceProvider? Find(string provider);

    /// <summary>
    /// The provisioning capability of a provider, which not every provider has.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <returns>The adapter, or null when the provider does not create issuers by API.</returns>
    IIssuerProvisioningProvider? FindProvisioning(string provider);

    /// <summary>
    /// The setup capability of a provider, which not every provider has.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <returns>The adapter, or null.</returns>
    IProviderSetupProvider? FindSetup(string provider);

    /// <summary>
    /// The capability of a provider to call this service with the result of a document, which not every provider has.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <returns>The adapter, or null.</returns>
    IProviderEventReader? FindEvents(string provider);
}
