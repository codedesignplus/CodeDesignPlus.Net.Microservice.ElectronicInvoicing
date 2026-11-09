namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Repositories;

/// <summary>
/// Mongo repository of the issuers.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class IssuerRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<IssuerRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), IIssuerRepository
{
    /// <inheritdoc/>
    public async Task<bool> CreateIfAbsentAsync(IssuerAggregate issuer, CancellationToken cancellationToken)
    {
        try
        {
            await CreateAsync(issuer, cancellationToken);

            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<List<Guid>> FindIdsByProviderAsync(string provider, CancellationToken cancellationToken)
    {
        var filter = Builders<IssuerAggregate>.Filter.Eq(x => x.Provider, provider);

        return GetCollection<IssuerAggregate>().Find(filter).Project(x => x.Id).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Mongo repository of the numbering ranges.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class NumberingRangeRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<NumberingRangeRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), INumberingRangeRepository
{
    /// <inheritdoc/>
    public async Task<bool> CreateIfAbsentAsync(NumberingRangeAggregate range, CancellationToken cancellationToken)
    {
        try
        {
            await CreateAsync(range, cancellationToken);

            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<NumberingRangeAggregate>> FindByTypeAsync(Guid tenant, ElectronicDocumentType type, CancellationToken cancellationToken)
    {
        var builder = Builders<NumberingRangeAggregate>.Filter;

        var filter = builder.Eq(x => x.Tenant, tenant) & builder.Eq(x => x.DocumentType, type) & builder.Eq(x => x.IsActive, true);

        return await GetCollection<NumberingRangeAggregate>().Find(filter).SortBy(x => x.ValidFrom).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<NumberingRangeAggregate>> FindWatchedAsync(LocalDate notEndedBefore, CancellationToken cancellationToken)
    {
        var builder = Builders<NumberingRangeAggregate>.Filter;

        var filter = builder.Eq(x => x.IsActive, true) & builder.Gte(x => x.ValidUntil, notEndedBefore);

        return await GetCollection<NumberingRangeAggregate>().Find(filter).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Mongo repository of the quotas of documents.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class DocumentQuotaRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<DocumentQuotaRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), IDocumentQuotaRepository
{
}

/// <summary>
/// Mongo repository of the usage of the providers' plans.
/// </summary>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="mongoOptions">The Mongo options.</param>
/// <param name="logger">The logger.</param>
public class ProviderPlanUsageRepository(IServiceProvider serviceProvider, IOptions<MongoOptions> mongoOptions, ILogger<ProviderPlanUsageRepository> logger)
    : RepositoryBase(serviceProvider, mongoOptions, logger), IProviderPlanUsageRepository
{
}
