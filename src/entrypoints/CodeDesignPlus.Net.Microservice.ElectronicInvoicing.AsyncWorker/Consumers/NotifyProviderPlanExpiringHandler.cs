using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// Tells the platform administrators that the contract with the provider ends soon (Kappali pendings/744): to their inbox
/// and, when one is configured, by email to the operations mailbox.
/// </summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="NotifyProviderPlanThresholdHandler"/>: this service owns the plan, the notice goes to the role
/// of the platform without tenant and the email to the mailbox of <see cref="PlatformNoticeOptions.OperationsMailbox"/>.
/// </para>
/// <para>
/// The event is raised once, by the round of the watch of the plan that first runs inside the window of
/// <see cref="ProviderPlanOptions.ExpiryNotice"/> before the end of the contract. The date of the text is the calendar day
/// of the end in the time zone of the plan. The ids of the notice and of the email come from the event, so a redelivery
/// repeats neither.
/// </para>
/// </remarks>
/// <param name="inbox">The inbox of ms-notification.</param>
/// <param name="pubSub">The bus, for the email.</param>
/// <param name="options">To whom the notices of the platform go.</param>
/// <param name="settings">The options of the service, for the time zone of the plan.</param>
/// <param name="logger">The logger.</param>
[QueueName<ProviderPlanUsageAggregate>("NotifyProviderPlanExpiringHandler")]
public class NotifyProviderPlanExpiringHandler(IInboxGrpc inbox, IPubSub pubSub, IOptions<PlatformNoticeOptions> options, IOptions<ElectronicInvoicingOptions> settings, ILogger<NotifyProviderPlanExpiringHandler> logger) : IEventHandler<ProviderPlanExpiringDomainEvent>
{
    private static readonly Guid EmailNamespace = Guid.Parse("3b8e5d14-27c9-4a6f-8e02-d1744f9c6a85");

    /// <summary>
    /// Sends the notice and, when there is a mailbox, the email.
    /// </summary>
    /// <param name="data">The end of the contract that is near.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The task.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the role of the platform administrators is not configured.</exception>
    public async Task HandleAsync(ProviderPlanExpiringDomainEvent data, CancellationToken token)
    {
        var role = options.Value.PlatformAdministratorRole;

        // Without the role the notice would reach nobody: it fails so the message is retried and shows in the log.
        if (role == Guid.Empty)
        {
            throw new InvalidOperationException($"'{PlatformNoticeOptions.Section}:{nameof(PlatformNoticeOptions.PlatformAdministratorRole)}' is not configured: the end of the contract with {data.Provider} on {data.ValidUntil} cannot be notified.");
        }

        var zone = DateTimeZoneProviders.Tzdb[settings.Value.ProviderPlan.TimeZoneId];
        var expiresOn = ProviderPlanExpiringText.ExpiresOn(data.ValidUntil, zone);
        var daysLeft = ProviderPlanExpiringText.DaysLeft(data.OccurredAt, data.ValidUntil, zone);
        var message = ProviderPlanExpiringText.Message(data.Provider, expiresOn, daysLeft);
        var kind = PlatformNotificationKinds.ElectronicInvoicingProviderPlanExpiring;

        var payload = new
        {
            provider = data.Provider,
            validUntil = data.ValidUntil.ToString("g", CultureInfo.InvariantCulture),
            expiresOn = expiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            daysLeft,
            used = data.Used,
            documents = data.Documents,
        };

        await inbox.NotifyRolesAsync(
            [role.ToString()],
            kind,
            ProviderPlanExpiringText.Title,
            message,
            ProviderPlanThresholdText.Module,
            data.AggregateId.ToString(),
            payload,
            // Without tenant: the platform administrator reads it outside all of them.
            Guid.Empty,
            // Sent by the system: this transversal service signs what is automatic this way.
            Guid.Empty,
            data.OccurredAt.ToDateTimeOffset(),
            NotificationIds.For(data.EventId, kind),
            token);

        logger.LogInformation("Provider plan of {Provider}: the end of the contract on {ExpiresOn} notified to the platform administrators ({DaysLeft} days left).", data.Provider, expiresOn, daysLeft);

        await EmailAsync(data, expiresOn, message, token);
    }

    /// <summary>
    /// Sends the email to the operations mailbox, when one is configured. It goes after the inbox: if it fails, the message is
    /// retried and the inbox, with the same id, does not repeat the notice.
    /// </summary>
    private async Task EmailAsync(ProviderPlanExpiringDomainEvent data, LocalDate expiresOn, string message, CancellationToken token)
    {
        var mailbox = options.Value.OperationsMailbox?.Trim();

        if (string.IsNullOrEmpty(mailbox) || string.IsNullOrWhiteSpace(options.Value.EmailTemplate))
        {
            logger.LogWarning("'{Section}' has no operations mailbox or email template: the end of the contract with {Provider} on {ExpiresOn} is notified only in the inbox.", PlatformNoticeOptions.Section, data.Provider, expiresOn);

            return;
        }

        var variables = new Dictionary<string, string>
        {
            ["subject"] = ProviderPlanExpiringText.EmailSubject(data.Provider, expiresOn),
            ["title"] = ProviderPlanExpiringText.Title,
            ["content"] = ProviderPlanThresholdText.EmailContent(message),
            ["organization_name"] = string.Empty,
            ["current_year"] = data.OccurredAt.InZone(DateTimeZoneProviders.Tzdb[settings.Value.ProviderPlan.TimeZoneId]).Year.ToString(CultureInfo.InvariantCulture),
        };

        // The id comes from the event and the mailbox: a redelivery publishes the same email, not a new one.
        var id = DeterministicGuid.Create(EmailNamespace, data.EventId.ToString(), mailbox);

        // Without tenant: the email is of the platform, like the notice.
        await pubSub.PublishAsync(new SendEmailDomainEvent(id, options.Value.EmailTemplate, [mailbox], [], [], variables, [], Guid.Empty, eventId: id), token);

        logger.LogInformation("Provider plan of {Provider}: the end of the contract on {ExpiresOn} emailed to the operations mailbox.", data.Provider, expiresOn);
    }
}
