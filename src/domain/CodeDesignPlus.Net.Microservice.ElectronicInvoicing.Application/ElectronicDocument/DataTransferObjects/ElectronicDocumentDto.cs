namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.DataTransferObjects;

/// <summary>
/// What the service shows of an electronic document. The amounts are in minor units of its currency.
/// </summary>
public class ElectronicDocumentDto : IDtoBase
{
    /// <summary>
    /// Id of the document, the one the caller derived from its source.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// Tenant that issues the document.
    /// </summary>
    public Guid Tenant { get; set; }

    /// <summary>
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; set; }

    /// <summary>
    /// The type of the document.
    /// </summary>
    public ElectronicDocumentType Type { get; set; }

    /// <summary>
    /// The caller's opaque reference.
    /// </summary>
    public string Reference { get; set; } = null!;

    /// <summary>
    /// The status of the document.
    /// </summary>
    public ElectronicDocumentStatus Status { get; set; }

    /// <summary>
    /// The prefix of the number, once it has one.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// The consecutive of the number, once it has one.
    /// </summary>
    public long? Consecutive { get; set; }

    /// <summary>
    /// The CUFE (CUDE in notes), once validated.
    /// </summary>
    public string? Cufe { get; set; }

    /// <summary>
    /// The issue date the provider put on the document.
    /// </summary>
    public LocalDate? IssueDate { get; set; }

    /// <summary>
    /// When the tax authority validated it.
    /// </summary>
    public Instant? ValidatedAt { get; set; }

    /// <summary>
    /// Who rejected it and why, as it came, while it is rejected.
    /// </summary>
    public DocumentRejection? Rejection { get; set; }

    /// <summary>
    /// The signed XML in ms-filestorage.
    /// </summary>
    public StoredFile? Xml { get; set; }

    /// <summary>
    /// The attached document of the tax authority in ms-filestorage.
    /// </summary>
    public StoredFile? AttachedDocument { get; set; }

    /// <summary>
    /// The graphic representation in ms-filestorage.
    /// </summary>
    public StoredFile? Pdf { get; set; }

    /// <summary>
    /// Whether the tax authority is not answering and the provider keeps it in contingency.
    /// </summary>
    public bool Contingency { get; set; }

    /// <summary>
    /// The last status the provider gave, as it came.
    /// </summary>
    public string? ProviderStatus { get; set; }

    /// <summary>
    /// How many sends or checks it took.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// When it is looked at again.
    /// </summary>
    public Instant? NextCheckAt { get; set; }

    /// <summary>
    /// What happened the last time it was looked at.
    /// </summary>
    public string? LastCheckOutcome { get; set; }

    /// <summary>
    /// When it ran out of retries, while it waits for someone to retry it.
    /// </summary>
    public Instant? StalledAt { get; set; }

    /// <summary>
    /// The document that must be validated before this one is sent.
    /// </summary>
    public Guid? PrecededBy { get; set; }

    /// <summary>
    /// What the document says.
    /// </summary>
    public DocumentContent Content { get; set; } = null!;

    /// <summary>
    /// When the document was created.
    /// </summary>
    public Instant CreatedAt { get; set; }

    /// <summary>
    /// The document as the service shows it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The DTO.</returns>
    /// <remarks>
    /// Field by field on purpose: a mapping by convention would publish by itself whatever a later member of the
    /// aggregate is called.
    /// </remarks>
    public static ElectronicDocumentDto From(ElectronicDocumentAggregate document) => new()
    {
        Id = document.Id,
        Tenant = document.Tenant,
        IssuerId = document.IssuerId,
        Type = document.Type,
        Reference = document.Reference,
        Status = document.Status,
        Prefix = document.Number?.Prefix,
        Consecutive = document.Number?.Consecutive,
        Cufe = document.Cufe,
        IssueDate = document.IssueDate,
        ValidatedAt = document.ValidatedAt,
        Rejection = document.Rejection,
        Xml = document.Files?.Xml,
        AttachedDocument = document.Files?.AttachedDocument,
        Pdf = document.Files?.Pdf,
        Contingency = document.Contingency,
        ProviderStatus = document.ProviderStatus,
        Attempts = document.Attempts,
        NextCheckAt = document.NextCheckAt,
        LastCheckOutcome = document.LastCheckOutcome,
        StalledAt = document.StalledAt,
        PrecededBy = document.PrecededBy,
        Content = document.Content,
        CreatedAt = document.CreatedAt
    };
}

/// <summary>
/// The answer to an issue: the document waits to be sent and its result arrives by event.
/// </summary>
/// <param name="Id">The id of the document.</param>
/// <param name="Status">Its status.</param>
/// <param name="Prefix">The prefix of its number, when it already has one.</param>
/// <param name="Consecutive">The consecutive of its number, when it already has one.</param>
public sealed record IssuedDocumentDto(Guid Id, ElectronicDocumentStatus Status, string? Prefix, long? Consecutive)
{
    /// <summary>
    /// The answer for a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The answer.</returns>
    public static IssuedDocumentDto From(ElectronicDocumentAggregate document) => new(document.Id, document.Status, document.Number?.Prefix, document.Number?.Consecutive);
}
