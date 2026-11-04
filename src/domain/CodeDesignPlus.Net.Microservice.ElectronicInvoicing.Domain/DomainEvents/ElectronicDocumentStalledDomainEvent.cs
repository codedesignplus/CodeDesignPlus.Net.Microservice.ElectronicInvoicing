namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// An electronic document ran out of retries without a final answer; this service stops insisting until someone asks
/// to retry it. Whoever originated it tells its role.
/// </summary>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="status">Where it stayed: waiting to be sent or sent without an answer.</param>
/// <param name="contingency">Whether the tax authority was not answering.</param>
/// <param name="attempts">How many sends or checks it took.</param>
/// <param name="lastProviderStatus">The last status the provider gave, as it came.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentStalledDomainEvent")]
public sealed class ElectronicDocumentStalledDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    ElectronicDocumentStatus status,
    bool contingency,
    int attempts,
    string? lastProviderStatus,
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
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; } = issuerId;

    /// <summary>
    /// The type of the document.
    /// </summary>
    public ElectronicDocumentType Type { get; } = type;

    /// <summary>
    /// The caller's opaque reference.
    /// </summary>
    public string Reference { get; } = reference;

    /// <summary>
    /// Where it stayed.
    /// </summary>
    public ElectronicDocumentStatus Status { get; } = status;

    /// <summary>
    /// Whether the tax authority was not answering.
    /// </summary>
    public bool Contingency { get; } = contingency;

    /// <summary>
    /// How many sends or checks it took.
    /// </summary>
    public int Attempts { get; } = attempts;

    /// <summary>
    /// The last status the provider gave, as it came.
    /// </summary>
    public string? LastProviderStatus { get; } = lastProviderStatus;
}
