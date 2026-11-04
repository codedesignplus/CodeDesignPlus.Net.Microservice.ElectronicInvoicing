namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// A numbering range is close to its last number or to the end of its validity: the issuer has to ask the tax authority
/// for a new resolution.
/// </summary>
/// <param name="aggregateId">The id of the range.</param>
/// <param name="tenant">The tenant of the issuer.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="documentType">The type of document the range numbers.</param>
/// <param name="prefix">The prefix of the range.</param>
/// <param name="alert">Whether the numbers or the validity are running out.</param>
/// <param name="remaining">How many numbers are left.</param>
/// <param name="validUntil">The last day of the validity.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<NumberingRangeAggregate>(1, "NumberingRangeRunningOutDomainEvent")]
public sealed class NumberingRangeRunningOutDomainEvent(
    Guid aggregateId,
    Guid tenant,
    Guid issuerId,
    ElectronicDocumentType documentType,
    string prefix,
    NumberingRangeAlert alert,
    long remaining,
    LocalDate validUntil,
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
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; } = issuerId;

    /// <summary>
    /// The type of document the range numbers.
    /// </summary>
    public ElectronicDocumentType DocumentType { get; } = documentType;

    /// <summary>
    /// The prefix of the range.
    /// </summary>
    public string Prefix { get; } = prefix;

    /// <summary>
    /// What is running out.
    /// </summary>
    public NumberingRangeAlert Alert { get; } = alert;

    /// <summary>
    /// How many numbers are left.
    /// </summary>
    public long Remaining { get; } = remaining;

    /// <summary>
    /// The last day of the validity.
    /// </summary>
    public LocalDate ValidUntil { get; } = validUntil;
}
