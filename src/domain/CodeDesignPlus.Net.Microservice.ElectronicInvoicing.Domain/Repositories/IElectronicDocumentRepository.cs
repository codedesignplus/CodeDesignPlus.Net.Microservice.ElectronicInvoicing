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
    /// <returns><c>false</c> when a previous request with the same id already saved it, or another document already carries its number.</returns>
    Task<bool> CreateIfAbsentAsync(ElectronicDocumentAggregate document, CancellationToken cancellationToken);

    /// <summary>
    /// The documents waiting to be sent or for an answer, and the validated ones still without their XML, whose next look is due, the oldest first.
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

    /// <summary>
    /// Whether a document of an issuer already carries a number for a type of document.
    /// </summary>
    /// <param name="issuerId">The id of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="number">The number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when a document uses it.</returns>
    Task<bool> IsNumberTakenAsync(Guid issuerId, ElectronicDocumentType type, DocumentNumber number, CancellationToken cancellationToken);

    /// <summary>
    /// The highest consecutive the documents of an issuer carry inside a range of numbers of a type and a prefix.
    /// </summary>
    /// <param name="issuerId">The id of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="from">The first consecutive of the range.</param>
    /// <param name="to">The last consecutive of the range.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The highest consecutive, or null when no document uses the range.</returns>
    Task<long?> FindHighestConsecutiveAsync(Guid issuerId, ElectronicDocumentType type, string prefix, long from, long to, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the indexes the documents need: the unique number of each issuer and type, which keeps two documents from
    /// carrying the same number however many requests write at once.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the indexes exist.</returns>
    Task EnsureIndexesAsync(CancellationToken cancellationToken);
}
