namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Repositories;

/// <summary>
/// The issuers, one per tenant.
/// </summary>
public interface IIssuerRepository : IRepositoryBase
{
    /// <summary>
    /// Saves a new issuer unless the tenant already has one.
    /// </summary>
    /// <param name="issuer">The issuer to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>false</c> when the tenant already had an issuer.</returns>
    Task<bool> CreateIfAbsentAsync(IssuerAggregate issuer, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of the issuers that issue with a provider.
    /// </summary>
    /// <param name="provider">The code of the provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ids.</returns>
    Task<List<Guid>> FindIdsByProviderAsync(string provider, CancellationToken cancellationToken);
}

/// <summary>
/// The numbering resolutions of the issuers.
/// </summary>
public interface INumberingRangeRepository : IRepositoryBase
{
    /// <summary>
    /// Saves a new range unless one with its id already exists.
    /// </summary>
    /// <param name="range">The range to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>false</c> when the range already existed.</returns>
    Task<bool> CreateIfAbsentAsync(NumberingRangeAggregate range, CancellationToken cancellationToken);

    /// <summary>
    /// The active ranges of an issuer for a type of document.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranges, the earliest validity first.</returns>
    Task<List<NumberingRangeAggregate>> FindByTypeAsync(Guid tenant, ElectronicDocumentType type, CancellationToken cancellationToken);

    /// <summary>
    /// The active ranges of every issuer whose validity has not ended before a day, for the watch.
    /// </summary>
    /// <param name="notEndedBefore">The earliest day a range may end and still be watched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranges.</returns>
    Task<List<NumberingRangeAggregate>> FindWatchedAsync(LocalDate notEndedBefore, CancellationToken cancellationToken);
}

/// <summary>
/// The quotas of documents of the issuers.
/// </summary>
public interface IDocumentQuotaRepository : IRepositoryBase
{
}

/// <summary>
/// The usage of the providers' plans.
/// </summary>
public interface IProviderPlanUsageRepository : IRepositoryBase
{
}
