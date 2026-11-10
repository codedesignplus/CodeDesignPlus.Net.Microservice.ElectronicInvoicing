namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// The files of a document validated without them were kept in ms-filestorage afterwards: whoever waited for them (to send
/// the document to the buyer) can take them now. A document validated with its files does not raise it; its
/// <see cref="ElectronicDocumentValidatedDomainEvent"/> already carries them.
/// </summary>
/// <remarks>
/// The callers filter by <see cref="Reference"/> what is theirs.
/// </remarks>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="prefix">The prefix of its number.</param>
/// <param name="consecutive">The consecutive of its number.</param>
/// <param name="xml">The signed XML in ms-filestorage.</param>
/// <param name="attachedDocument">The attached document the tax authority answered with, when it could be kept.</param>
/// <param name="pdf">The graphic representation, when it could be kept.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentFilesKeptDomainEvent")]
public sealed class ElectronicDocumentFilesKeptDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    string? prefix,
    long? consecutive,
    StoredFile xml,
    StoredFile? attachedDocument,
    StoredFile? pdf,
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
    /// The prefix of its number.
    /// </summary>
    public string? Prefix { get; } = prefix;

    /// <summary>
    /// The consecutive of its number.
    /// </summary>
    public long? Consecutive { get; } = consecutive;

    /// <summary>
    /// The signed XML in ms-filestorage.
    /// </summary>
    public StoredFile Xml { get; } = xml;

    /// <summary>
    /// The attached document in ms-filestorage.
    /// </summary>
    public StoredFile? AttachedDocument { get; } = attachedDocument;

    /// <summary>
    /// The graphic representation in ms-filestorage.
    /// </summary>
    public StoredFile? Pdf { get; } = pdf;
}
