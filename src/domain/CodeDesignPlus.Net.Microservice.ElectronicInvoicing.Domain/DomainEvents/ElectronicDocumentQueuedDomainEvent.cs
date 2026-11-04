namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// An electronic document was created or corrected and waits to be sent to the provider.
/// </summary>
/// <remarks>
/// The worker of this same service consumes it and sends the document; the status job is the safety net if the event is
/// lost.
/// </remarks>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="precededBy">The document that must be validated before this one is sent.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentQueuedDomainEvent")]
public sealed class ElectronicDocumentQueuedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    ElectronicDocumentType type,
    string reference,
    Guid? precededBy,
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
    /// The type of the document.
    /// </summary>
    public ElectronicDocumentType Type { get; } = type;

    /// <summary>
    /// The caller's opaque reference.
    /// </summary>
    public string Reference { get; } = reference;

    /// <summary>
    /// The document that must be validated before this one is sent.
    /// </summary>
    public Guid? PrecededBy { get; } = precededBy;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="aggregateId">The id of the document.</param>
    /// <param name="tenant">The tenant that issues the document.</param>
    /// <param name="type">The type of the document.</param>
    /// <param name="reference">The caller's opaque reference.</param>
    /// <param name="precededBy">The document that must be validated first.</param>
    /// <returns>The new event.</returns>
    public static ElectronicDocumentQueuedDomainEvent Create(Guid aggregateId, Guid tenant, ElectronicDocumentType type, string reference, Guid? precededBy) => new(aggregateId, tenant, type, reference, precededBy);
}
