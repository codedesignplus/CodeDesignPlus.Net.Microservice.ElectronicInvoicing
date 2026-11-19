namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// A tenant was allowed to issue with a test provider: its documents may be ones that never reach a tax authority.
/// </summary>
/// <param name="aggregateId">The id of the permission.</param>
/// <param name="tenant">The tenant allowed.</param>
/// <param name="grantedBy">Who granted it.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<TestProviderGrantAggregate>(1, "TestProviderGrantedDomainEvent")]
public sealed class TestProviderGrantedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid grantedBy,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The tenant allowed.
    /// </summary>
    public Guid Tenant { get; } = tenant;

    /// <summary>
    /// Who granted it.
    /// </summary>
    public Guid GrantedBy { get; } = grantedBy;
}

/// <summary>
/// A tenant is no longer allowed to issue with a test provider: its issuer stops issuing with one.
/// </summary>
/// <param name="aggregateId">The id of the permission.</param>
/// <param name="tenant">The tenant that lost it.</param>
/// <param name="revokedBy">Who revoked it.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<TestProviderGrantAggregate>(1, "TestProviderRevokedDomainEvent")]
public sealed class TestProviderRevokedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid revokedBy,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The tenant that lost it.
    /// </summary>
    public Guid Tenant { get; } = tenant;

    /// <summary>
    /// Who revoked it.
    /// </summary>
    public Guid RevokedBy { get; } = revokedBy;
}
