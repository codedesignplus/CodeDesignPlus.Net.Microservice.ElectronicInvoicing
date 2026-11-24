using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// Tells the platform administrators that the documents of the month crossed a threshold of the plan contracted with the
/// provider (Kappali pendings/744): to their inbox and, when one is configured, by email to the operations mailbox.
/// </summary>
/// <remarks>
/// <para>
/// This service consumes it because it owns the plan and its usage; ms-notification and ms-emails only deliver. The notice
/// goes to the role of the platform, without tenant, because the plan is of the platform and whoever reads it works outside
/// every tenant. The email does not go to the people of the role (there is no contract to list who has a role) but to the
/// mailbox of <see cref="PlatformNoticeOptions.OperationsMailbox"/>.
/// </para>
/// <para>
/// Nothing is blocked: the documents over the included ones are issued and billed by the provider. From 100 % the text says
/// so and carries the estimated cost of the overage, formatted with the currency of ms-locations. The ids of the notice and
/// of the email come from the event, so a redelivery repeats neither.
/// </para>
/// </remarks>
/// <param name="inbox">The inbox of ms-notification.</param>
/// <param name="pubSub">The bus, for the email.</param>
/// <param name="currencies">The currencies of ms-locations, to write the estimated cost.</param>
/// <param name="options">To whom the notices of the platform go.</param>
/// <param name="logger">The logger.</param>
[QueueName<ProviderPlanUsageAggregate>("NotifyProviderPlanThresholdHandler")]
public class NotifyProviderPlanThresholdHandler(IInboxGrpc inbox, IPubSub pubSub, ICurrencyGrpc currencies, IOptions<PlatformNoticeOptions> options, ILogger<NotifyProviderPlanThresholdHandler> logger) : IEventHandler<ProviderPlanThresholdReachedDomainEvent>
{
    private static readonly Guid EmailNamespace = Guid.Parse("6f1c3a52-8d47-4e0b-b9a1-2c744e5d7f31");

    /// <summary>
    /// Sends the notice and, when there is a mailbox, the email.
    /// </summary>
    /// <param name="data">The threshold crossed.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The task.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the role of the platform administrators is not configured.</exception>
    public async Task HandleAsync(ProviderPlanThresholdReachedDomainEvent data, CancellationToken token)
    {
        var role = options.Value.PlatformAdministratorRole;

        // Without the role the notice would reach nobody: it fails so the message is retried and shows in the log.
        if (role == Guid.Empty)
        {
            throw new InvalidOperationException($"'{PlatformNoticeOptions.Section}:{nameof(PlatformNoticeOptions.PlatformAdministratorRole)}' is not configured: the threshold {data.Percentage} % of the provider plan of {data.Month} cannot be notified.");
        }

        var cost = ProviderPlanThresholdText.IsExhausted(data.Percentage) ? await CostAsync(data, token) : null;
        var message = ProviderPlanThresholdText.Message(data.Month, data.Percentage, data.Used, data.Documents, data.Overage, cost);
        var kind = PlatformNotificationKinds.ElectronicInvoicingProviderPlanThreshold;

        // The amounts go unformatted in the payload, for the frontend to write them with the locale of the reader.
        var payload = new
        {
            month = data.Month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            provider = data.Provider,
            threshold = data.Percentage,
            percentage = ProviderPlanThresholdText.Percentage(data.Used, data.Documents, data.Percentage),
            used = data.Used,
            documents = data.Documents,
            overage = data.Overage,
            estimatedOverageCost = data.EstimatedOverageCost,
            currency = data.Currency,
            periodEnd = data.PeriodEnd,
        };

        await inbox.NotifyRolesAsync(
            [role.ToString()],
            kind,
            ProviderPlanThresholdText.Title(data.Percentage),
            message,
            ProviderPlanThresholdText.Module,
            data.AggregateId.ToString(),
            payload,
            // Without tenant: the platform administrator reads it outside all of them.
            Guid.Empty,
            // Sent by the system: this transversal service signs what is automatic this way.
            Guid.Empty,
            data.OccurredAt.ToDateTimeOffset(),
            NotificationIds.For(data.EventId, $"{kind}/{data.Percentage.ToString(CultureInfo.InvariantCulture)}"),
            token);

        logger.LogInformation("Provider plan of {Month}: threshold {Threshold} % notified to the platform administrators ({Used} of {Documents}).", data.Month, data.Percentage, data.Used, data.Documents);

        await EmailAsync(data, message, token);
    }

    /// <summary>
    /// The estimated cost of the overage, written with its currency.
    /// </summary>
    private async Task<string> CostAsync(ProviderPlanThresholdReachedDomainEvent data, CancellationToken token)
    {
        var currency = await currencies.GetCurrencyAsync(code: data.Currency, cancellationToken: token) ?? throw new InvalidOperationException($"The currency '{data.Currency}' of the provider plan does not exist in ms-locations: the threshold {data.Percentage} % of {data.Month} cannot be notified.");

        return ProviderPlanThresholdText.Amount(data.EstimatedOverageCost, currency);
    }

    /// <summary>
    /// Sends the email to the operations mailbox, when one is configured. It goes after the inbox: if it fails, the message is
    /// retried and the inbox, with the same id, does not repeat the notice.
    /// </summary>
    private async Task EmailAsync(ProviderPlanThresholdReachedDomainEvent data, string message, CancellationToken token)
    {
        var mailbox = options.Value.OperationsMailbox?.Trim();

        if (string.IsNullOrEmpty(mailbox) || string.IsNullOrWhiteSpace(options.Value.EmailTemplate))
        {
            logger.LogWarning("'{Section}' has no operations mailbox or email template: the threshold {Threshold} % of the provider plan of {Month} is notified only in the inbox.", PlatformNoticeOptions.Section, data.Percentage, data.Month);

            return;
        }

        var subject = ProviderPlanThresholdText.EmailSubject(data.Month, data.Percentage);
        var variables = new Dictionary<string, string>
        {
            ["subject"] = subject,
            ["title"] = ProviderPlanThresholdText.Title(data.Percentage),
            ["content"] = ProviderPlanThresholdText.EmailContent(message),
            ["organization_name"] = string.Empty,
            ["current_year"] = data.Month.Year.ToString(CultureInfo.InvariantCulture),
        };

        // The id comes from the event and the mailbox: a redelivery publishes the same email, not a new one.
        var id = DeterministicGuid.Create(EmailNamespace, data.EventId.ToString(), mailbox);

        // Without tenant: the email is of the platform, like the notice.
        await pubSub.PublishAsync(new SendEmailDomainEvent(id, options.Value.EmailTemplate, [mailbox], [], [], variables, [], Guid.Empty, eventId: id), token);

        logger.LogInformation("Provider plan of {Month}: threshold {Threshold} % emailed to the operations mailbox.", data.Month, data.Percentage);
    }
}
