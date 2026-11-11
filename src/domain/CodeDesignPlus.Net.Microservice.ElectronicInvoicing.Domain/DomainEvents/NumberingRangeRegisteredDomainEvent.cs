namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// A numbering resolution of an issuer was registered, or synchronized from the provider and something of it changed:
/// a caller that numbers its own documents keeps its sequence with this resolution and never writes it by hand.
/// </summary>
/// <remarks>
/// It carries the whole resolution as it stands now, so a consumer creates or replaces its copy with the last event it
/// receives for <see cref="DomainEvent.AggregateId"/>.
/// </remarks>
/// <param name="aggregateId">The id of the range.</param>
/// <param name="tenant">The tenant of the issuer; the reserved tenant of the platform when <paramref name="platformIssuer"/> is set.</param>
/// <param name="platformIssuer">Whether the range is of the platform's own issuer and not of a tenant.</param>
/// <param name="issuerId">The id of the issuer.</param>
/// <param name="documentType">The type of document the range numbers.</param>
/// <param name="prefix">The prefix authorized.</param>
/// <param name="resolutionNumber">The number of the resolution.</param>
/// <param name="technicalKey">The technical key of the resolution, which goes into the CUFE; null when it has none.</param>
/// <param name="from">The first number authorized.</param>
/// <param name="to">The last number authorized.</param>
/// <param name="validFrom">The first day of the validity, in the time zone of the issuer.</param>
/// <param name="validUntil">The last day of the validity, in the time zone of the issuer.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey<NumberingRangeAggregate>(1, "NumberingRangeRegisteredDomainEvent")]
public sealed class NumberingRangeRegisteredDomainEvent(
    Guid aggregateId,
    Guid tenant,
    bool platformIssuer,
    Guid issuerId,
    ElectronicDocumentType documentType,
    string prefix,
    string resolutionNumber,
    string? technicalKey,
    long from,
    long to,
    LocalDate validFrom,
    LocalDate validUntil,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// Tenant of the issuer; the reserved tenant of the platform for the platform's issuer.
    /// </summary>
    public Guid Tenant { get; } = tenant;

    /// <summary>
    /// Whether the range is of the platform's own issuer and not of a tenant.
    /// </summary>
    public bool PlatformIssuer { get; } = platformIssuer;

    /// <summary>
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; } = issuerId;

    /// <summary>
    /// The type of document the range numbers.
    /// </summary>
    public ElectronicDocumentType DocumentType { get; } = documentType;

    /// <summary>
    /// The prefix authorized.
    /// </summary>
    public string Prefix { get; } = prefix;

    /// <summary>
    /// The number of the resolution.
    /// </summary>
    public string ResolutionNumber { get; } = resolutionNumber;

    /// <summary>
    /// The technical key of the resolution; null when it has none.
    /// </summary>
    public string? TechnicalKey { get; } = technicalKey;

    /// <summary>
    /// The first number authorized.
    /// </summary>
    public long From { get; } = from;

    /// <summary>
    /// The last number authorized.
    /// </summary>
    public long To { get; } = to;

    /// <summary>
    /// The first day of the validity.
    /// </summary>
    public LocalDate ValidFrom { get; } = validFrom;

    /// <summary>
    /// The last day of the validity.
    /// </summary>
    public LocalDate ValidUntil { get; } = validUntil;
}
