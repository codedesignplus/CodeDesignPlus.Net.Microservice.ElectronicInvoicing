namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// The tax authority validated an electronic document, but its files ran out of retries without being kept in
/// ms-filestorage; this service stops asking for them until someone retries the document. The issuer must keep the XML,
/// so whoever originated the document tells its role.
/// </summary>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issues the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="prefix">The prefix of its number.</param>
/// <param name="consecutive">The consecutive of its number.</param>
/// <param name="cufe">The CUFE (CUDE in notes).</param>
/// <param name="attempts">How many times the files were asked for.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentFilesStalledDomainEvent")]
public sealed class ElectronicDocumentFilesStalledDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    string? prefix,
    long? consecutive,
    string? cufe,
    int attempts,
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
    /// The CUFE (CUDE in notes).
    /// </summary>
    public string? Cufe { get; } = cufe;

    /// <summary>
    /// How many times the files were asked for.
    /// </summary>
    public int Attempts { get; } = attempts;
}
