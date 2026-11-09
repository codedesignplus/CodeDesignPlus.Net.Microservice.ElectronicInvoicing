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
        var filter = builder.In(x => x.Status, [ElectronicDocumentStatus.Draft, ElectronicDocumentStatus.Sent])
            & builder.Lte(x => x.NextCheckAt, now)
            & builder.Eq(x => x.StalledAt, null)
            & builder.Eq(x => x.IsActive, true);

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
}
