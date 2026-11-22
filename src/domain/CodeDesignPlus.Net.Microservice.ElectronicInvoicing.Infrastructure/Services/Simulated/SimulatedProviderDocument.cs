using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// A document as the simulated provider keeps it: what it received, the rule it applied and how many times its state
/// was asked for.
/// </summary>
/// <remarks>
/// <para>
/// It is the memory of the simulated provider, and it lives in the database because the service runs several replicas
/// and restarts: the replica that answers a check is rarely the one that received the document. It is not an aggregate
/// of the domain: nothing of the service reads it but the simulated provider, as nothing reads what a real provider
/// keeps.
/// </para>
/// <para>
/// Its id derives from the document, so sending the same document again writes the same row. The rule is kept as it
/// was when the document arrived: changing <c>Simulated:Rules</c> does not change what already happened to a document.
/// </para>
/// <para>
/// Every member has a setter and none goes through a constructor, so a member added later does not break the reading
/// of the rows already stored.
/// </para>
/// </remarks>
public sealed class SimulatedProviderDocument : IEntityBase
{
    /// <summary>
    /// The id the simulated provider gives the document: derived from <see cref="DocumentId"/>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant of the issuer that sent it.
    /// </summary>
    public Guid Tenant { get; set; }

    /// <summary>
    /// The issuer that sent it.
    /// </summary>
    public Guid IssuerId { get; set; }

    /// <summary>
    /// The id of the document in this service.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// The type of the document.
    /// </summary>
    public ElectronicDocumentType Type { get; set; }

    /// <summary>
    /// The number it was sent with.
    /// </summary>
    public DocumentNumber? Number { get; set; }

    /// <summary>
    /// The resolution of its number, which the graphic representation prints.
    /// </summary>
    public SimulatedNumberingRange? NumberingRange { get; set; }

    /// <summary>
    /// What the document says, as it was sent last.
    /// </summary>
    public DocumentContent Content { get; set; } = null!;

    /// <summary>
    /// The day the simulated provider received it first, in the time zone of the issuer: its issue date.
    /// </summary>
    public LocalDate IssueDate { get; set; }

    /// <summary>
    /// What the rule applied on its last send does with it.
    /// </summary>
    public SimulatedOutcome Outcome { get; set; }

    /// <summary>
    /// The reason of the rejection, when the rule rejects it.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// How many checks the rule makes it wait before it is accepted.
    /// </summary>
    public int ChecksUntilResolved { get; set; }

    /// <summary>
    /// How many times its state was asked for since its last send. Only the database moves it, one by one.
    /// </summary>
    public int Checks { get; set; }

    /// <summary>
    /// When the simulated provider received it first.
    /// </summary>
    public Instant FirstReceivedAt { get; set; }

    /// <summary>
    /// When the simulated provider received it last.
    /// </summary>
    public Instant ReceivedAt { get; set; }

    /// <summary>
    /// When the database may delete the row: <c>Simulated:Retention</c> after its last send. Null when the simulation
    /// keeps what it receives forever.
    /// </summary>
    /// <remarks>
    /// The date is kept in each row, and not as an age in the index, so changing the retention needs no change of the
    /// index: it applies to what arrives from then on.
    /// </remarks>
    public Instant? ExpiresAt { get; set; }

    /// <summary>
    /// The document as the adapters describe it, for what writes its files and its email.
    /// </summary>
    /// <returns>The document, with the key of its sends derived again from its id.</returns>
    public ProviderDocument ToProviderDocument() => new(DocumentId, Type, Number, NumberingRange?.ToProviderRange(), Content, IdempotencyKeys.For(DocumentId), IssueDate);
}

/// <summary>
/// The resolution of the number of a document, as the simulated provider keeps it.
/// </summary>
public sealed class SimulatedNumberingRange
{
    /// <summary>
    /// The technical key.
    /// </summary>
    public string? TechnicalKey { get; set; }

    /// <summary>
    /// The prefix.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// The number of the resolution.
    /// </summary>
    public string ResolutionNumber { get; set; } = string.Empty;

    /// <summary>
    /// The first number.
    /// </summary>
    public long From { get; set; }

    /// <summary>
    /// The last number.
    /// </summary>
    public long To { get; set; }

    /// <summary>
    /// The first day of the validity.
    /// </summary>
    public LocalDate ValidFrom { get; set; }

    /// <summary>
    /// The last day of the validity.
    /// </summary>
    public LocalDate ValidUntil { get; set; }

    /// <summary>
    /// The resolution a document was sent with, to keep it.
    /// </summary>
    /// <param name="range">The resolution, when the document brings one.</param>
    /// <returns>The resolution to keep, or null.</returns>
    public static SimulatedNumberingRange? Of(ProviderNumberingRange? range) => range is null ? null : new SimulatedNumberingRange
    {
        TechnicalKey = range.TechnicalKey,
        Prefix = range.Prefix,
        ResolutionNumber = range.ResolutionNumber,
        From = range.From,
        To = range.To,
        ValidFrom = range.ValidFrom,
        ValidUntil = range.ValidUntil
    };

    /// <summary>
    /// The resolution as the adapters describe it.
    /// </summary>
    /// <returns>The resolution.</returns>
    public ProviderNumberingRange ToProviderRange() => new(TechnicalKey, Prefix, ResolutionNumber, From, To, ValidFrom, ValidUntil);
}
