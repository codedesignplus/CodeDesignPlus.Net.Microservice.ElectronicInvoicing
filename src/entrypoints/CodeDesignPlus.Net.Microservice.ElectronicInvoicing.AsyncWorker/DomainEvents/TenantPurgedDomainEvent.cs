namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.DomainEvents;

/// <summary>
/// ms-tenants purged a deleted tenant whose period to restore it expired.
/// </summary>
/// <remarks>
/// Only what is used is declared: the id of the tenant. The rest of the event is ignored when it is deserialized.
/// </remarks>
/// <param name="aggregateId">The id of the tenant.</param>
/// <param name="name">The name of the tenant.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey("TenantAggregate", 1, "TenantPurgedDomainEvent", "ms-tenants", "codedesignplus")]
public class TenantPurgedDomainEvent(
    Guid aggregateId,
    string name,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// Name of the tenant.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="aggregateId">The id of the tenant.</param>
    /// <param name="name">The name of the tenant.</param>
    /// <returns>The new event.</returns>
    public static TenantPurgedDomainEvent Create(Guid aggregateId, string name)
    {
        return new TenantPurgedDomainEvent(aggregateId, name);
    }
}
