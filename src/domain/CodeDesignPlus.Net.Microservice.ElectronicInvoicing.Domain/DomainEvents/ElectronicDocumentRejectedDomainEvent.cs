namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// The provider or the tax authority rejected an electronic document. The caller may correct it on the same number.
/// </summary>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issued the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="prefix">The prefix of the number, when it had one.</param>
/// <param name="consecutive">The consecutive of the number, when it had one.</param>
/// <param name="rejection">Who rejected it and why, as it came.</param>
/// <param name="attempts">How many sends or checks it took.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentRejectedDomainEvent")]
public sealed class ElectronicDocumentRejectedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    string? prefix,
    long? consecutive,
    DocumentRejection rejection,
    int attempts,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// Tenant that issued the document.
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
    /// The prefix of the number.
    /// </summary>
    public string? Prefix { get; } = prefix;

    /// <summary>
    /// The consecutive of the number.
    /// </summary>
    public long? Consecutive { get; } = consecutive;

    /// <summary>
    /// Who rejected it and why.
    /// </summary>
    public DocumentRejection Rejection { get; } = rejection;

    /// <summary>
    /// How many sends or checks it took.
    /// </summary>
    public int Attempts { get; } = attempts;
}
