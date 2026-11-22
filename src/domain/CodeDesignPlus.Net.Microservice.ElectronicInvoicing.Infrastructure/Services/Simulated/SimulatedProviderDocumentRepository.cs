namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// Where the simulated provider keeps the documents it received, shared by every replica of the service.
/// </summary>
/// <remarks>
/// Every write is a single atomic operation of the database, so two replicas that receive or check the same document at
/// once lose nothing: the simulated provider never reads a document, changes it in memory and saves it.
/// </remarks>
public interface ISimulatedProviderDocumentRepository
{
    /// <summary>
    /// Keeps a document the simulated provider received. Receiving the same document again replaces what it says and its
    /// rule and starts its checks over, and keeps the day and the instant it arrived first.
    /// </summary>
    /// <param name="document">The document as it arrived.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document as it is kept.</returns>
    Task<SimulatedProviderDocument> ReceiveAsync(SimulatedProviderDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one more check of the state of a document and reads it.
    /// </summary>
    /// <param name="key">How to find the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document with the check already counted, or null when the simulated provider never received it.</returns>
    Task<SimulatedProviderDocument?> CheckAsync(SimulatedDocumentKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a document without counting a check.
    /// </summary>
    /// <param name="key">How to find the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document, or null when the simulated provider never received it.</returns>
    Task<SimulatedProviderDocument?> FindAsync(SimulatedDocumentKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the index that lets the database delete each document once its <see cref="SimulatedProviderDocument.ExpiresAt"/> passes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the index exists.</returns>
    Task EnsureIndexesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// How to find a document in the simulated provider: by the id it gave it and, failing that, by its number among the
/// documents of its issuer. Always inside the tenant of the issuer that asks.
/// </summary>
/// <param name="Tenant">The tenant of the issuer that asks.</param>
/// <param name="IssuerId">The issuer that asks.</param>
/// <param name="Id">The id the simulated provider gave the document, when it is known and it is one of its ids.</param>
/// <param name="Type">The type of the document.</param>
/// <param name="Number">The number of the document, when it has one.</param>
public sealed record SimulatedDocumentKey(Guid Tenant, Guid IssuerId, Guid? Id, ElectronicDocumentType Type, DocumentNumber? Number);

/// <summary>
/// Mongo repository of the documents the simulated provider received.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class SimulatedProviderDocumentRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<SimulatedProviderDocumentRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), ISimulatedProviderDocumentRepository
{
    /// <summary>
    /// The code Mongo answers a command with when a unique key already exists (E11000). It is a code of the server.
    /// </summary>
    private const int DuplicateKeyCode = 11000;

    /// <inheritdoc/>
    public async Task<SimulatedProviderDocument> ReceiveAsync(SimulatedProviderDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var filter = Builders<SimulatedProviderDocument>.Filter.Eq(x => x.Id, document.Id);

        // One operation: what is sent is replaced and the checks start over; what belongs to the first arrival is only
        // written when the row is created.
        var update = Builders<SimulatedProviderDocument>.Update
            .Set(x => x.Tenant, document.Tenant)
            .Set(x => x.IssuerId, document.IssuerId)
            .Set(x => x.DocumentId, document.DocumentId)
            .Set(x => x.Type, document.Type)
            .Set(x => x.Number, document.Number)
            .Set(x => x.NumberingRange, document.NumberingRange)
            .Set(x => x.Content, document.Content)
            .Set(x => x.Outcome, document.Outcome)
            .Set(x => x.Reason, document.Reason)
            .Set(x => x.ChecksUntilResolved, document.ChecksUntilResolved)
            .Set(x => x.Checks, 0)
            .Set(x => x.ReceivedAt, document.ReceivedAt)
            .Set(x => x.ExpiresAt, document.ExpiresAt)
            .SetOnInsert(x => x.IssueDate, document.IssueDate)
            .SetOnInsert(x => x.FirstReceivedAt, document.FirstReceivedAt);

        var options = new FindOneAndUpdateOptions<SimulatedProviderDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After };

        try
        {
            return await GetCollection<SimulatedProviderDocument>().FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        }
        catch (MongoException exception) when (IsDuplicateKey(exception))
        {
            // Two replicas created the same row at once and this one lost: the row exists now, so this time it updates
            // it. MongoDB repeats such an upsert by itself, so this is never reached against it; a compatible engine is
            // not bound to.
            return await GetCollection<SimulatedProviderDocument>().FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<SimulatedProviderDocument?> CheckAsync(SimulatedDocumentKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        // The database adds the check: two replicas asking at once count two, whichever answers first.
        var count = Builders<SimulatedProviderDocument>.Update.Inc(x => x.Checks, 1);
        var options = new FindOneAndUpdateOptions<SimulatedProviderDocument> { ReturnDocument = ReturnDocument.After };

        foreach (var filter in FiltersOf(key))
        {
            var found = await GetCollection<SimulatedProviderDocument>().FindOneAndUpdateAsync(filter, count, options, cancellationToken);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<SimulatedProviderDocument?> FindAsync(SimulatedDocumentKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var filter in FiltersOf(key))
        {
            var found = await GetCollection<SimulatedProviderDocument>().Find(filter).FirstOrDefaultAsync(cancellationToken);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        // The row says when it expires, so the index adds no time of its own. A row without the date never expires.
        var expiry = new CreateIndexModel<SimulatedProviderDocument>(
            Builders<SimulatedProviderDocument>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { Name = ExpiryIndexName, ExpireAfter = TimeSpan.Zero });

        return GetCollection<SimulatedProviderDocument>().Indexes.CreateOneAsync(expiry, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The name of the index that expires the documents.
    /// </summary>
    public const string ExpiryIndexName = "ExpiresAt_ttl";

    /// <summary>
    /// The filters that find a document, in the order they are tried: its id first, then its number.
    /// </summary>
    private static IEnumerable<FilterDefinition<SimulatedProviderDocument>> FiltersOf(SimulatedDocumentKey key)
    {
        var builder = Builders<SimulatedProviderDocument>.Filter;

        if (key.Id is { } id)
        {
            yield return builder.Eq(x => x.Id, id) & builder.Eq(x => x.Tenant, key.Tenant);
        }

        if (key.Number is { } number)
        {
            yield return builder.Eq(x => x.Tenant, key.Tenant)
                & builder.Eq(x => x.IssuerId, key.IssuerId)
                & builder.Eq(x => x.Type, key.Type)
                & builder.Eq(x => x.Number!.Prefix, number.Prefix)
                & builder.Eq(x => x.Number!.Consecutive, number.Consecutive);
        }
    }

    private static bool IsDuplicateKey(MongoException exception) => exception switch
    {
        MongoWriteException write => write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        MongoCommandException command => command.Code == DuplicateKeyCode,
        _ => false
    };
}
