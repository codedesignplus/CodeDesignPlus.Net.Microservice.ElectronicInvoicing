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
}
