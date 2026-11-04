namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// An issuer was enabled: from now on its documents have legal effects (or go to the enablement tests).
/// </summary>
/// <param name="aggregateId">The id of the issuer.</param>
/// <param name="tenant">The tenant of the issuer.</param>
/// <param name="provider">The provider it issues with.</param>
/// <param name="environment">The environment its documents are sent to.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<IssuerAggregate>(1, "IssuerEnabledDomainEvent")]
public sealed class IssuerEnabledDomainEvent(
    Guid aggregateId,
    Guid tenant,
    string provider,
    IssuerEnvironment environment,
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
    /// The provider it issues with.
    /// </summary>
    public string Provider { get; } = provider;

    /// <summary>
    /// The environment its documents are sent to.
    /// </summary>
    public IssuerEnvironment Environment { get; } = environment;
}

/// <summary>
/// An issuer was suspended: it does not issue until it is enabled again.
/// </summary>
/// <param name="aggregateId">The id of the issuer.</param>
/// <param name="tenant">The tenant of the issuer.</param>
/// <param name="provider">The provider it issued with.</param>
/// <param name="reasonCode">Code of the error catalog that says why, so each reader shows it in its language.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<IssuerAggregate>(1, "IssuerSuspendedDomainEvent")]
public sealed class IssuerSuspendedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    string provider,
    string reasonCode,
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
    /// The provider it issued with.
    /// </summary>
    public string Provider { get; } = provider;

    /// <summary>
    /// Code of the error catalog that says why.
    /// </summary>
    public string ReasonCode { get; } = reasonCode;
}
