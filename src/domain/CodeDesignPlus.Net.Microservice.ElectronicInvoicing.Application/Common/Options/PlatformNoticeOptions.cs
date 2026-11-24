namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;

/// <summary>
/// To whom the notices of the platform go: the thresholds of the plan contracted with the provider (Kappali pendings/744).
/// </summary>
/// <remarks>
/// The role is an id of the catalog of ms-roles and the mailbox is of the operator of the platform: both change with the
/// product and the environment, so they live in the <c>appsettings</c> of the worker and not in the code. This service is
/// transversal and does not know the roles of any product.
/// </remarks>
public class PlatformNoticeOptions
{
    /// <summary>
    /// Name of the section in the configuration.
    /// </summary>
    public static readonly string Section = "PlatformNotices";

    /// <summary>
    /// The role of the platform administrators, who receive the notices in their inbox.
    /// </summary>
    public Guid PlatformAdministratorRole { get; set; }

    /// <summary>
    /// The operations mailbox that also receives the notices by email.
    /// </summary>
    /// <remarks>
    /// Empty means no email: the notice goes only to the inbox of <see cref="PlatformAdministratorRole"/>.
    /// </remarks>
    public string OperationsMailbox { get; set; } = string.Empty;

    /// <summary>
    /// The template of ms-emails of the email: the generic one of notices, with subject, title and content.
    /// </summary>
    public string EmailTemplate { get; set; } = string.Empty;
}
