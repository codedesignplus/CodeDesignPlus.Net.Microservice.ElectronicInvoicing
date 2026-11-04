namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// The documents an issuer has used crossed a threshold of its quota (for example 80 % or 100 %). The service that
/// sells the quota tells its role; this one does not know who that is.
/// </summary>
/// <param name="aggregateId">The id of the quota.</param>
/// <param name="tenant">The tenant of the issuer.</param>
/// <param name="percentage">The threshold crossed, as a percentage of the quota.</param>
/// <param name="used">Documents used in the period.</param>
/// <param name="limit">Documents the quota allows in the period.</param>
/// <param name="validUntil">When the period ends.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<DocumentQuotaAggregate>(1, "DocumentQuotaThresholdReachedDomainEvent")]
public sealed class DocumentQuotaThresholdReachedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    int percentage,
    long used,
    long limit,
    Instant validUntil,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// Tenant of the issuer.
    /// </summary>
    public Guid Tenant { get; } = tenant;

    /// <summary>
    /// The threshold crossed, as a percentage of the quota.
    /// </summary>
    public int Percentage { get; } = percentage;

    /// <summary>
    /// Documents used in the period.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents the quota allows in the period.
    /// </summary>
    public long Limit { get; } = limit;

    /// <summary>
    /// When the period ends.
    /// </summary>
    public Instant ValidUntil { get; } = validUntil;
}

/// <summary>
/// The documents sent with the provider's plan crossed a threshold of the plan (for example 70 % or 90 %).
/// </summary>
/// <param name="aggregateId">The id of the plan usage.</param>
/// <param name="provider">The provider of the plan.</param>
/// <param name="percentage">The threshold crossed, as a percentage of the plan.</param>
/// <param name="used">Documents sent in the plan's period.</param>
/// <param name="documents">Documents the plan includes.</param>
/// <param name="validUntil">When the plan ends.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ProviderPlanUsageAggregate>(1, "ProviderPlanThresholdReachedDomainEvent")]
public sealed class ProviderPlanThresholdReachedDomainEvent(
    Guid aggregateId,
    string provider,
    int percentage,
    long used,
    long documents,
    Instant validUntil,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; } = provider;

    /// <summary>
    /// The threshold crossed, as a percentage of the plan.
    /// </summary>
    public int Percentage { get; } = percentage;

    /// <summary>
    /// Documents sent in the plan's period.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents the plan includes.
    /// </summary>
    public long Documents { get; } = documents;

    /// <summary>
    /// When the plan ends.
    /// </summary>
    public Instant ValidUntil { get; } = validUntil;
}

/// <summary>
/// The contract with the provider ends soon.
/// </summary>
/// <param name="aggregateId">The id of the plan usage.</param>
/// <param name="provider">The provider of the plan.</param>
/// <param name="used">Documents sent in the plan's period.</param>
/// <param name="documents">Documents the plan includes.</param>
/// <param name="validUntil">When the plan ends.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ProviderPlanUsageAggregate>(1, "ProviderPlanExpiringDomainEvent")]
public sealed class ProviderPlanExpiringDomainEvent(
    Guid aggregateId,
    string provider,
    long used,
    long documents,
    Instant validUntil,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; } = provider;

    /// <summary>
    /// Documents sent in the plan's period.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents the plan includes.
    /// </summary>
    public long Documents { get; } = documents;

    /// <summary>
    /// When the plan ends.
    /// </summary>
    public Instant ValidUntil { get; } = validUntil;
}
