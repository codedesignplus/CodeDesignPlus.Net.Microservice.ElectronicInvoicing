namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// An electronic document was created for an issuer.
/// </summary>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentCreatedDomainEvent")]
public sealed class ElectronicDocumentCreatedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    string reference,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// Tenant that issues the document.
    /// </summary>
    public Guid Tenant { get; } = tenant;

    /// <summary>
    /// The caller's opaque reference.
    /// </summary>
    public string Reference { get; } = reference;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="aggregateId">The id of the document.</param>
    /// <param name="tenant">The tenant that issues the document.</param>
    /// <param name="reference">The caller's opaque reference.</param>
    /// <returns>The new event.</returns>
    public static ElectronicDocumentCreatedDomainEvent Create(Guid aggregateId, Guid tenant, string reference)
    {
        return new ElectronicDocumentCreatedDomainEvent(aggregateId, tenant, reference);
    }
}
