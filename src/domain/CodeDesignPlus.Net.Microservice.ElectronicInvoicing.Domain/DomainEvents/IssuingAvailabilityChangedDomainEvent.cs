namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// How an issuer stands after something changed whether it can issue a new document: its quota was used up or has room
/// again (a unit given back, a bigger or a smaller allowance, a period that starts), or the issuer itself stopped or
/// started being able to issue (a mandate, the tests, an enablement, a suspension, the permission of a test provider).
/// </summary>
/// <remarks>
/// <para>
/// It carries the whole state and not a difference, so whoever listens keeps the last one (by
/// <see cref="DomainEvent.OccurredAt"/>) and needs no other event: <see cref="CanIssue"/> answers «would an invoice or
/// a ticket be received now?». A note needs no room in the quota: it is received while <see cref="IssuerCanIssue"/>.
/// </para>
/// <para>
/// It is published when that answer changes and when the allowance changes, not on every document: while there is room,
/// <see cref="Used"/> and <see cref="Remaining"/> are the ones of the moment of the event and grow old. Nothing is
/// published when time alone changes the answer: the quota has no room from <see cref="PeriodEnd"/> on, unless
/// <see cref="RenewedUntil"/> says the allowance was renewed, and then from <see cref="PeriodEnd"/> until
/// <see cref="RenewedUntil"/> the count starts again from zero with the same <see cref="Limit"/>.
/// </para>
/// <para>
/// The thresholds of the quota (<see cref="DocumentQuotaThresholdReachedDomainEvent"/>) are still announced apart: they
/// are a notice for a person, and this is the state for another service.
/// </para>
/// </remarks>
/// <param name="aggregateId">The id of the issuer of the tenant, which is derived from the tenant even before it is registered.</param>
/// <param name="tenant">The tenant of the issuer.</param>
/// <param name="canIssue">Whether an invoice or a ticket would be received now: the issuer can issue and its quota has room.</param>
/// <param name="reasonCode">Code of the error catalog of this service that says why not; null when it can issue.</param>
/// <param name="issuerRegistered">Whether the tenant has an issuer.</param>
/// <param name="issuerCanIssue">Whether the issuer can issue whatever its quota says: it has a mandate, it is enabled or in its tests, and its provider is allowed to it.</param>
/// <param name="issuerStatus">The status of the issuer by its name (<c>Draft</c>, <c>Provisioned</c>, <c>Testing</c>, <c>Enabled</c>, <c>Suspended</c>); null when there is none.</param>
/// <param name="hasRoom">Whether the quota has room for an invoice or a ticket now.</param>
/// <param name="limit">Documents the quota allows in the period.</param>
/// <param name="used">Documents used in the period, notes included.</param>
/// <param name="remaining">Documents an invoice or a ticket can still use now.</param>
/// <param name="periodStart">When the period of the quota started; null without allowance.</param>
/// <param name="periodEnd">When the period of the quota ends; null without allowance.</param>
/// <param name="renewedUntil">The end of the next period, when the allowance was renewed before this one ended.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<IssuerAggregate>(1, "IssuingAvailabilityChangedDomainEvent")]
public sealed class IssuingAvailabilityChangedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    bool canIssue,
    string? reasonCode,
    bool issuerRegistered,
    bool issuerCanIssue,
    string? issuerStatus,
    bool hasRoom,
    long limit,
    long used,
    long remaining,
    Instant? periodStart,
    Instant? periodEnd,
    Instant? renewedUntil,
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
    /// Whether an invoice or a ticket would be received now.
    /// </summary>
    public bool CanIssue { get; } = canIssue;

    /// <summary>
    /// Code of the error catalog that says why not; null when it can issue.
    /// </summary>
    public string? ReasonCode { get; } = reasonCode;

    /// <summary>
    /// Whether the tenant has an issuer.
    /// </summary>
    public bool IssuerRegistered { get; } = issuerRegistered;

    /// <summary>
    /// Whether the issuer can issue whatever its quota says.
    /// </summary>
    public bool IssuerCanIssue { get; } = issuerCanIssue;

    /// <summary>
    /// The status of the issuer by its name; null when there is none.
    /// </summary>
    public string? IssuerStatus { get; } = issuerStatus;

    /// <summary>
    /// Whether the quota has room for an invoice or a ticket now.
    /// </summary>
    public bool HasRoom { get; } = hasRoom;

    /// <summary>
    /// Documents the quota allows in the period.
    /// </summary>
    public long Limit { get; } = limit;

    /// <summary>
    /// Documents used in the period, notes included.
    /// </summary>
    public long Used { get; } = used;

    /// <summary>
    /// Documents an invoice or a ticket can still use now.
    /// </summary>
    public long Remaining { get; } = remaining;

    /// <summary>
    /// When the period of the quota started; null without allowance.
    /// </summary>
    public Instant? PeriodStart { get; } = periodStart;

    /// <summary>
    /// When the period of the quota ends; null without allowance.
    /// </summary>
    public Instant? PeriodEnd { get; } = periodEnd;

    /// <summary>
    /// The end of the next period, when the allowance was renewed before this one ended.
    /// </summary>
    public Instant? RenewedUntil { get; } = renewedUntil;
}
