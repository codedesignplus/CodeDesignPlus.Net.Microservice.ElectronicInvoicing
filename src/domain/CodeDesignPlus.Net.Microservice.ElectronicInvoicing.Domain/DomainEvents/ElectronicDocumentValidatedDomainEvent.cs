namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// The tax authority validated an electronic document: it has its CUFE and it is final.
/// </summary>
/// <remarks>
/// The callers filter by <see cref="Reference"/> what is theirs.
/// </remarks>
/// <param name="aggregateId">The id of the document.</param>
/// <param name="tenant">The tenant that issued the document.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="type">The type of the document.</param>
/// <param name="reference">The caller's opaque reference.</param>
/// <param name="prefix">The prefix of the number.</param>
/// <param name="consecutive">The consecutive of the number.</param>
/// <param name="cufe">The CUFE (CUDE in notes) given by the tax authority.</param>
/// <param name="issueDate">The issue date the provider put on the document.</param>
/// <param name="validatedAt">When it was validated.</param>
/// <param name="xml">The signed XML in ms-filestorage, when it could be stored.</param>
/// <param name="pdf">The graphic representation in ms-filestorage, when it could be stored.</param>
/// <param name="totals">The totals of the document.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<ElectronicDocumentAggregate>(1, "ElectronicDocumentValidatedDomainEvent")]
public sealed class ElectronicDocumentValidatedDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType type,
    string reference,
    string prefix,
    long consecutive,
    string cufe,
    LocalDate? issueDate,
    Instant validatedAt,
    StoredFile? xml,
    StoredFile? pdf,
    DocumentTotals totals,
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
    public string Prefix { get; } = prefix;

    /// <summary>
    /// The consecutive of the number.
    /// </summary>
    public long Consecutive { get; } = consecutive;

    /// <summary>
    /// The CUFE (CUDE in notes).
    /// </summary>
    public string Cufe { get; } = cufe;

    /// <summary>
    /// The issue date the provider put on the document.
    /// </summary>
    public LocalDate? IssueDate { get; } = issueDate;

    /// <summary>
    /// When it was validated.
    /// </summary>
    public Instant ValidatedAt { get; } = validatedAt;

    /// <summary>
    /// The signed XML in ms-filestorage.
    /// </summary>
    public StoredFile? Xml { get; } = xml;

    /// <summary>
    /// The graphic representation in ms-filestorage.
    /// </summary>
    public StoredFile? Pdf { get; } = pdf;

    /// <summary>
    /// The totals of the document, in minor units.
    /// </summary>
    public DocumentTotals Totals { get; } = totals;

    /// <summary>
    /// The attached document the tax authority answered with (the signed XML with its validation), in ms-filestorage, when
    /// it could be stored: with the graphic representation it is what the buyer receives. Not in the constructor so the
    /// event stays additive for its consumers.
    /// </summary>
    public StoredFile? AttachedDocument { get; init; }

    /// <summary>
    /// The due date the validated document carries, in the time zone of the issuer: the one asked for, or the issue
    /// date when that was earlier (a cash sale, or a credit whose due date went by before the provider issued it), so the
    /// caller adopts it. Null when the provider did not say the issue date and nothing was asked for. Not in the
    /// constructor so the event stays additive for its consumers.
    /// </summary>
    public LocalDate? DueDate { get; init; }
}
