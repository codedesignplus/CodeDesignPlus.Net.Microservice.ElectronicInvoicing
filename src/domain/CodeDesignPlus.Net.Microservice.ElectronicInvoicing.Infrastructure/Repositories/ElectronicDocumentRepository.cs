namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Repositories;

/// <summary>
/// Mongo repository of the electronic documents.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class ElectronicDocumentRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<ElectronicDocumentRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), IElectronicDocumentRepository
{
    /// <inheritdoc/>
    public async Task<bool> CreateIfAbsentAsync(ElectronicDocumentAggregate document, CancellationToken cancellationToken)
    {
        try
        {
            await CreateAsync(document, cancellationToken);

            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<ElectronicDocumentAggregate>> FindDueAsync(Instant now, int limit, CancellationToken cancellationToken)
    {
        var builder = Builders<ElectronicDocumentAggregate>.Filter;

        // `In` over the two states that wait, and not `Ne` over the final ones: a state added later would otherwise enter
        // the round by omission.
        var waiting = builder.In(x => x.Status, [ElectronicDocumentStatus.Draft, ElectronicDocumentStatus.Sent])
            & builder.Lte(x => x.NextCheckAt, now)
            & builder.Eq(x => x.StalledAt, null);

        // A validated document without its XML asks for its files again. The ones validated before the files were
        // retried have no next look (null): they are due as they are, with no backfill.
        var missingFiles = builder.Eq(x => x.Status, ElectronicDocumentStatus.Validated)
            & builder.Eq(x => x.Files!.Xml, null)
            & builder.Eq(x => x.FilesStalledAt, null)
            & (builder.Eq(x => x.NextCheckAt, null) | builder.Lte(x => x.NextCheckAt, now));

        var filter = (waiting | missingFiles) & builder.Eq(x => x.IsActive, true);

        // The oldest first: a round cut by the limit goes on where it stopped instead of looking at the same ones.
        return await GetCollection<ElectronicDocumentAggregate>()
            .Find(filter)
            .SortBy(x => x.NextCheckAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ElectronicDocumentAggregate?> FindByProviderDocumentIdAsync(string providerDocumentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerDocumentId))
        {
            return null;
        }

        return await GetCollection<ElectronicDocumentAggregate>()
            .Find(Builders<ElectronicDocumentAggregate>.Filter.Eq(x => x.ProviderDocumentId, providerDocumentId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<ElectronicDocumentAggregate>> FindWaitingOnAsync(Guid precededBy, CancellationToken cancellationToken)
    {
        var builder = Builders<ElectronicDocumentAggregate>.Filter;

        var filter = builder.Eq(x => x.PrecededBy, precededBy) & builder.Eq(x => x.Status, ElectronicDocumentStatus.Draft);

        return await GetCollection<ElectronicDocumentAggregate>().Find(filter).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<long> CountFirstSentBetweenAsync(Instant from, Instant until, IReadOnlyCollection<Guid> issuerIds, CancellationToken cancellationToken)
    {
        var builder = Builders<ElectronicDocumentAggregate>.Filter;

        var filter = builder.Gte(x => x.FirstSentAt, from) & builder.Lt(x => x.FirstSentAt, until) & builder.In(x => x.IssuerId, issuerIds ?? []);

        return GetCollection<ElectronicDocumentAggregate>().CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> IsNumberTakenAsync(Guid issuerId, ElectronicDocumentType type, DocumentNumber number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(number);

        var builder = Builders<ElectronicDocumentAggregate>.Filter;

        var filter = builder.Eq(x => x.IssuerId, issuerId) & builder.Eq(x => x.Type, type) & builder.Eq(x => x.Number!.Prefix, number.Prefix) & builder.Eq(x => x.Number!.Consecutive, number.Consecutive);

        return GetCollection<ElectronicDocumentAggregate>().Find(filter).AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long?> FindHighestConsecutiveAsync(Guid issuerId, ElectronicDocumentType type, string prefix, long from, long to, CancellationToken cancellationToken)
    {
        var builder = Builders<ElectronicDocumentAggregate>.Filter;

        var filter = builder.Eq(x => x.IssuerId, issuerId)
            & builder.Eq(x => x.Type, type)
            & builder.Eq(x => x.Number!.Prefix, prefix)
            & builder.Gte(x => x.Number!.Consecutive, from)
            & builder.Lte(x => x.Number!.Consecutive, to);

        var highest = await GetCollection<ElectronicDocumentAggregate>()
            .Find(filter)
            .SortByDescending(x => x.Number!.Consecutive)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);

        return highest?.Number?.Consecutive;
    }

    /// <inheritdoc/>
    public Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        // Only the documents with a number enter the index: a draft waiting for its number has none, and several of them
        // must not collide on an empty number.
        var number = new CreateIndexModel<ElectronicDocumentAggregate>(
            Builders<ElectronicDocumentAggregate>.IndexKeys
                .Ascending(x => x.IssuerId)
                .Ascending(x => x.Type)
                .Ascending(x => x.Number!.Prefix)
                .Ascending(x => x.Number!.Consecutive),
            new CreateIndexOptions<ElectronicDocumentAggregate>
            {
                Name = NumberIndexName,
                Unique = true,
                PartialFilterExpression = Builders<ElectronicDocumentAggregate>.Filter.Exists(x => x.Number!.Consecutive)
            });

        return GetCollection<ElectronicDocumentAggregate>().Indexes.CreateOneAsync(number, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The name of the unique index of the numbers.
    /// </summary>
    public const string NumberIndexName = "IssuerId_Type_Number_unique";
}
