namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// When a tenant is purged, deletes everything this service keeps of it.
/// </summary>
/// <remarks>
/// ms-tenants publishes it once the period to restore a deleted tenant expires, and it may arrive more than once:
/// deleting what is no longer there is harmless. Until then the documents are kept.
/// <para>
/// <c>PurgeTenantDataHandlerTest</c> walks the domain and requires every type with a <c>Tenant</c> to be purged: a new
/// aggregate that is not added here makes the test fail.
/// </para>
/// </remarks>
/// <param name="repository">The repository of the documents.</param>
/// <param name="logger">The logger.</param>
[QueueName<ElectronicDocumentAggregate>("PurgeTenantDataHandler")]
public class PurgeTenantDataHandler(IElectronicDocumentRepository repository, ILogger<PurgeTenantDataHandler> logger) : IEventHandler<TenantPurgedDomainEvent>
{
    /// <summary>
    /// Deletes the data of the purged tenant.
    /// </summary>
    /// <param name="data">The event of ms-tenants.</param>
    /// <param name="token">The cancellation token.</param>
    public async Task HandleAsync(TenantPurgedDomainEvent data, CancellationToken token)
    {
        var tenant = data.AggregateId;

        var documents = await repository.DeleteByTenantAsync<ElectronicDocumentAggregate>(tenant, token);

        logger.LogInformation("Tenant {TenantId} purged: {Documents} electronic documents deleted", tenant, documents);
    }
}
