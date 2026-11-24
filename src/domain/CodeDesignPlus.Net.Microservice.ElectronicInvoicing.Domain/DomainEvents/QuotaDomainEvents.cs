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
/// The documents of a month crossed a threshold of the ones the provider's plan includes in the month (for example 70 %,
/// 90 % or 100 %). Nothing is blocked: the documents over the included ones are issued and billed by the provider.
/// </summary>
/// <param name="aggregateId">The id of the usage of the month.</param>
/// <param name="provider">The provider of the plan.</param>
/// <param name="percentage">The threshold crossed, as a percentage of the included documents.</param>
/// <param name="used">Documents of the month.</param>
/// <param name="documents">Documents the plan includes in the month.</param>
/// <param name="periodEnd">When the counted month ends, exclusive.</param>
/// <param name="month">The first day of the month, in the time zone of the plan.</param>
/// <param name="overage">Documents of the month over the included ones.</param>
/// <param name="estimatedOverageCost">What they are estimated to cost, in minor units of the currency.</param>
/// <param name="currency">ISO 4217 code of the currency of the plan.</param>
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
    Instant periodEnd,
    LocalDate month,
    long overage,
    long estimatedOverageCost,
    string currency,
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
    /// The threshold crossed, as a percentage of the included documents.
    /// </summary>
    public int Percentage { get; } = percentage;

    /// <summary>
    /// Documents of the month.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents the plan includes in the month.
    /// </summary>
    public long Documents { get; } = documents;

    /// <summary>
    /// When the counted month ends, exclusive.
    /// </summary>
    public Instant PeriodEnd { get; } = periodEnd;

    /// <summary>
    /// The first day of the month, in the time zone of the plan.
    /// </summary>
    public LocalDate Month { get; } = month;

    /// <summary>
    /// Documents of the month over the included ones.
    /// </summary>
    public long Overage { get; } = overage;

    /// <summary>
    /// What the documents over the included ones are estimated to cost, in minor units of <see cref="Currency"/>.
    /// </summary>
    public long EstimatedOverageCost { get; } = estimatedOverageCost;

    /// <summary>
    /// ISO 4217 code of the currency of the plan.
    /// </summary>
    public string Currency { get; } = currency;
}

/// <summary>
/// The contract with the provider ends soon.
/// </summary>
/// <param name="aggregateId">The id of the usage of the month that announces it.</param>
/// <param name="provider">The provider of the plan.</param>
/// <param name="used">Documents of that month.</param>
/// <param name="documents">Documents the plan includes in a month.</param>
/// <param name="validUntil">When the contract ends.</param>
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
    /// Documents of the month that announces it.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents the plan includes in a month.
    /// </summary>
    public long Documents { get; } = documents;

    /// <summary>
    /// When the contract ends.
    /// </summary>
    public Instant ValidUntil { get; } = validUntil;
}
