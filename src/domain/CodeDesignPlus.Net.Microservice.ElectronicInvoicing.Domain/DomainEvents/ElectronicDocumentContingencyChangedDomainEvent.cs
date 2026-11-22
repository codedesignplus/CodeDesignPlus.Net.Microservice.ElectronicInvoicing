namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// A sent electronic document entered or left the contingency while it still waits for its final answer: the tax
/// authority stopped answering, or it answers again and the document is still being processed. Whoever originated it
/// can tell its role why the document takes longer.
/// </summary>
/// <remarks>
/// It is published only when the value changes, never on each look. A document that is validated or rejected leaves the
/// contingency with that event and not with this one.
/// </remarks>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="contingency">Whether the tax authority is not answering now.</param>
/// <param name="attempts">How many sends or checks it took so far.</param>
/// <param name="providerStatus">The status the provider gave, as it came.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentContingencyChangedDomainEvent")]
public sealed class ElectronicDocumentContingencyChangedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    bool contingency,
    int attempts,
    string? providerStatus,
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
    /// Whether the tax authority is not answering now.
    /// </summary>
    public bool Contingency { get; } = contingency;

    /// <summary>
    /// How many sends or checks it took so far.
    /// </summary>
    public int Attempts { get; } = attempts;

    /// <summary>
    /// The status the provider gave, as it came.
    /// </summary>
    public string? ProviderStatus { get; } = providerStatus;
}
