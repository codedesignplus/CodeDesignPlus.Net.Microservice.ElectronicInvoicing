namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Repositories;

/// <summary>
/// Electronic documents of every issuer.
/// </summary>
public interface IElectronicDocumentRepository : IRepositoryBase
{
    /// <summary>
    /// Saves a new document unless one with its id already exists.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>false</c> when a previous request with the same id already saved it.</returns>
    Task<bool> CreateIfAbsentAsync(ElectronicDocumentAggregate document, CancellationToken cancellationToken);

    /// <summary>
    /// The documents waiting to be sent or for an answer whose next look is due, the oldest first.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The due documents of every tenant.</returns>
    Task<List<ElectronicDocumentAggregate>> FindDueAsync(Instant now, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// The document a provider knows by its id.
    /// </summary>
    /// <param name="providerDocumentId">The id in the provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document, or null when none has that id.</returns>
    Task<ElectronicDocumentAggregate?> FindByProviderDocumentIdAsync(string providerDocumentId, CancellationToken cancellationToken);

    /// <summary>
    /// The drafts that wait for a document to be validated before they are sent.
    /// </summary>
    /// <param name="precededBy">The id of the document they wait for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The drafts.</returns>
    Task<List<ElectronicDocumentAggregate>> FindWaitingOnAsync(Guid precededBy, CancellationToken cancellationToken);

    /// <summary>
    /// How many documents of some issuers their provider received for the first time in a period.
    /// </summary>
    /// <param name="from">The start of the period.</param>
    /// <param name="until">The end of the period, excluded.</param>
    /// <param name="issuerIds">The issuers whose documents count: those of the provider of a plan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The count.</returns>
    Task<long> CountFirstSentBetweenAsync(Instant from, Instant until, IReadOnlyCollection<Guid> issuerIds, CancellationToken cancellationToken);
}
