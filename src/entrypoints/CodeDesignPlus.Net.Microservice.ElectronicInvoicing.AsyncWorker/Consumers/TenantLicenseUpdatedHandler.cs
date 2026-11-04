using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// When the license of a tenant changes, its quota of documents follows the attributes the configuration names.
/// </summary>
/// <remarks>
/// ms-tenants publishes the effective attributes (license and packages combined) every time they change, and again on a
/// renewal. The event may arrive more than once or late: the quota ignores an allowance older than the one it applied.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[QueueName<DocumentQuotaAggregate>("TenantLicenseUpdatedHandler")]
public class TenantLicenseUpdatedHandler(IMediator mediator, ILogger<TenantLicenseUpdatedHandler> logger) : IEventHandler<TenantLicenseUpdatedDomainEvent>
{
    /// <summary>
    /// Applies the quota.
    /// </summary>
    /// <param name="data">The event of ms-tenants.</param>
    /// <param name="token">The cancellation token.</param>
    public async Task HandleAsync(TenantLicenseUpdatedDomainEvent data, CancellationToken token)
    {
        if (data.License is null)
        {
            logger.LogWarning("Tenant {TenantId}: the license event came without a license; the quota is not changed.", data.AggregateId);

            return;
        }

        var changed = await mediator.Send(new ApplyLicenseQuotaCommand(data.AggregateId, data.License.Metadata ?? [], data.License.EndDate, data.OccurredAt), token);

        logger.LogInformation("Tenant {TenantId}: quota of electronic documents {Outcome}.", data.AggregateId, changed ? "updated" : "unchanged");
    }
}
