namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.DataTransferObjects;

/// <summary>
/// What the service shows of an electronic document.
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
    /// The caller's opaque reference.
    /// </summary>
    public string Reference { get; set; } = null!;

    /// <summary>
    /// When the document was created.
    /// </summary>
    public Instant CreatedAt { get; set; }
}
