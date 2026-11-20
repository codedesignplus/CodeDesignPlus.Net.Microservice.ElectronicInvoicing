namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

/// <summary>
/// Asks ms-emails to send an email with one of its templates. It is the event ms-emails listens to (its key names the
/// aggregate and the service of ms-emails, not ones of this service), so the email goes out without this service calling
/// anyone.
/// </summary>
/// <remarks>
/// ms-emails looks for the template by its name in the tenant and, when the tenant has none, among the ones of the
/// system; and it reads each attachment from <c>{target}/{id}/{name}</c> in the tenant's folder of ms-filestorage.
/// </remarks>
/// <param name="aggregateId">The id of the request.</param>
/// <param name="templateName">The name of the template in ms-emails.</param>
/// <param name="to">The addresses the email goes to.</param>
/// <param name="cc">The addresses that get a copy.</param>
/// <param name="bcc">The addresses that get a blind copy.</param>
/// <param name="variables">The values of the variables of the template.</param>
/// <param name="attachments">The files of ms-filestorage the email carries.</param>
/// <param name="tenant">The tenant that sends the email and owns the attachments.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey("EmailAggregate", 1, "SendEmailDomainEvent", "ms-emails")]
public sealed class SendEmailDomainEvent(
    Guid aggregateId,
    string templateName,
    List<string> to,
    List<string> cc,
    List<string> bcc,
    Dictionary<string, string> variables,
    List<EmailAttachment> attachments,
    Guid tenant,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The name of the template in ms-emails.
    /// </summary>
    public string TemplateName { get; } = templateName;

    /// <summary>
    /// The addresses the email goes to.
    /// </summary>
    public List<string> To { get; } = to;

    /// <summary>
    /// The addresses that get a copy.
    /// </summary>
    public List<string> Cc { get; } = cc;

    /// <summary>
    /// The addresses that get a blind copy.
    /// </summary>
    public List<string> Bcc { get; } = bcc;

    /// <summary>
    /// The values of the variables of the template.
    /// </summary>
    public Dictionary<string, string> Variables { get; } = variables;

    /// <summary>
    /// The files of ms-filestorage the email carries.
    /// </summary>
    public List<EmailAttachment> Attachments { get; } = attachments;

    /// <summary>
    /// The tenant that sends the email and owns the attachments.
    /// </summary>
    public Guid Tenant { get; } = tenant;
}

/// <summary>
/// A file of ms-filestorage that an email carries, as ms-emails reads it.
/// </summary>
/// <param name="Id">The id of the file in ms-filestorage.</param>
/// <param name="Name">The name of the file, with its extension.</param>
/// <param name="Target">The folder of ms-filestorage where it is kept.</param>
public sealed record EmailAttachment(Guid Id, string Name, string Target);
