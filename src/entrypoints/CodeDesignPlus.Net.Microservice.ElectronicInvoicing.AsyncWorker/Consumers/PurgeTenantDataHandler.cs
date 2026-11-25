using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// When a tenant is purged, deletes everything this service keeps of it.
/// </summary>
/// <remarks>
/// ms-tenants publishes it once the period to restore a deleted tenant expires, and it may arrive more than once:
/// deleting what is no longer there is harmless. Until then the documents are kept, by decision of the user (2026-11-02):
/// there is no download offered on deletion, the purge of ms-tenants rules. The issuer goes with the rest; the
/// provider keeps its own copy of the documents.
/// <para>
/// The repository deletes by type: the same one purges every collection of the service. <c>PurgeTenantDataHandlerTest</c>
/// walks the domain and the infrastructure and requires every type with a <c>Tenant</c> to be purged: a new aggregate
/// that is not added here makes the test fail. What the simulated provider received goes too: it is its copy of the
/// documents, and it lives in this service.
/// </para>
/// </remarks>
/// <param name="repository">The repository, which deletes the documents of any collection of the service by tenant.</param>
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

        // The platform's issuer is not a tenant of ms-tenants: an event naming its reserved id never purges it.
        if (PlatformScope.Owns(tenant))
        {
            logger.LogWarning("A purge named the tenant reserved for the platform's issuer; nothing is deleted");

            return;
        }

        var documents = await repository.DeleteByTenantAsync<ElectronicDocumentAggregate>(tenant, token);
        var ranges = await repository.DeleteByTenantAsync<NumberingRangeAggregate>(tenant, token);
        var quotas = await repository.DeleteByTenantAsync<DocumentQuotaAggregate>(tenant, token);
        var usages = await repository.DeleteByTenantAsync<ProviderPlanUsageAggregate>(tenant, token);
        var issuers = await repository.DeleteByTenantAsync<IssuerAggregate>(tenant, token);
        var grants = await repository.DeleteByTenantAsync<TestProviderGrantAggregate>(tenant, token);
        var simulated = await repository.DeleteByTenantAsync<SimulatedProviderDocument>(tenant, token);
        var profiles = await repository.DeleteByTenantAsync<ManagedFiscalProfileAggregate>(tenant, token);

        logger.LogInformation("Tenant {TenantId} purged: {Documents} electronic documents, {Ranges} numbering ranges, {Quotas} quotas, {Usages} plan usages, {Issuers} issuers, {Grants} test provider permissions, {Simulated} documents of the simulated provider and {Profiles} managed fiscal data deleted", tenant, documents, ranges, quotas, usages, issuers, grants, simulated, profiles);
    }
}
