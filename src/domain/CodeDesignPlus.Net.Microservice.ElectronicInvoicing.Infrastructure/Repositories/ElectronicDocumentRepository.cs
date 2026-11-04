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
}
