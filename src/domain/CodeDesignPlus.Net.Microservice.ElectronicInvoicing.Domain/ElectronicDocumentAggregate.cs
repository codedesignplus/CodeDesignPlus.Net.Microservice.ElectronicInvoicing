namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// An electronic document that an issuer sends to the tax authority through a technology provider.
/// </summary>
/// <remarks>
/// Its id is the one the caller derives from its own source, so repeating the request does not issue twice. For now
/// it only carries the issuing tenant and the caller's opaque reference; the content, the numbering and the lifecycle
/// against the provider come with the first phase of the service.
/// </remarks>
public class ElectronicDocumentAggregate(Guid id) : AggregateRootBase(id)
{
    /// <summary>
    /// Tenant that issues the document.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// Opaque text of the caller that comes back in the events, so each caller can tell which documents are its own.
    /// </summary>
    public string Reference { get; private set; } = null!;

    private ElectronicDocumentAggregate(Guid id, Guid tenant, string reference, Guid createdBy) : this(id)
    {
        this.Tenant = tenant;
        this.Reference = reference;
        this.IsActive = true;
        this.CreatedBy = createdBy;
        this.CreatedAt = SystemClock.Instance.GetCurrentInstant();

        AddEvent(ElectronicDocumentCreatedDomainEvent.Create(Id, Tenant, Reference));
    }

    /// <summary>
    /// Creates an electronic document for the given tenant.
    /// </summary>
    /// <param name="id">The id the caller derived from its own source.</param>
    /// <param name="tenant">The tenant that issues the document.</param>
    /// <param name="reference">The caller's opaque reference.</param>
    /// <param name="createdBy">The user who asked for the document.</param>
    /// <returns>The new document, with its creation event.</returns>
    public static ElectronicDocumentAggregate Create(Guid id, Guid tenant, string reference, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(id, Errors.ElectronicDocumentIdIsRequired);
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.IsNullOrEmpty(reference, Errors.ReferenceIsRequired);
        DomainGuard.IsTrue(reference.Length > FieldLength.Title, Errors.ReferenceIsTooLong);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        return new ElectronicDocumentAggregate(id, tenant, reference, createdBy);
    }
}
