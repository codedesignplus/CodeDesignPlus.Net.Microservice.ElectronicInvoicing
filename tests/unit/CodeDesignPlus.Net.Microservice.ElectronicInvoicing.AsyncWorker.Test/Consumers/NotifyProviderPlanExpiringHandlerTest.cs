using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.gRpc.Clients.Services.Notification;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.PubSub.Abstractions;
using NodaTime;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Test.Consumers;

/// <summary>
/// The notice of the end of the contract with the provider (Kappali pendings/744): it goes to the inbox of the role of the
/// platform, without tenant, says the provider, the day the contract ends in the time zone of the plan and the days left;
/// with an operations mailbox it is also emailed.
/// </summary>
public class NotifyProviderPlanExpiringHandlerTest
{
    private static readonly Guid PlatformRole = Guid.Parse("20000000-0000-0000-0000-000000000010");
    private static readonly Guid UsageId = Guid.Parse("aa000000-0000-4000-8000-000000000745");
    private static readonly Instant ContractEnd = Instant.FromUtc(2027, 11, 4, 5, 0);
    private const string Mailbox = "operations@example.com";

    private readonly Mock<IInboxGrpc> inbox = new();
    private readonly Mock<IPubSub> pubSub = new();
    private readonly List<NotificationRequest> notifications = [];
    private readonly List<SendEmailDomainEvent> emails = [];

    public NotifyProviderPlanExpiringHandlerTest()
    {
        inbox.Setup(x => x.NotifyAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => notifications.Add(request))
            .Returns(Task.CompletedTask);
        pubSub.Setup(x => x.PublishAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((@event, _) => emails.Add((SendEmailDomainEvent)@event))
            .Returns(Task.CompletedTask);
    }

    private NotifyProviderPlanExpiringHandler Handler(Guid? role = null, string mailbox = Mailbox) => new(
        inbox.Object, pubSub.Object,
        MsOptions.Create(new PlatformNoticeOptions { PlatformAdministratorRole = role ?? PlatformRole, OperationsMailbox = mailbox, EmailTemplate = "Notice" }),
        MsOptions.Create(new ElectronicInvoicingOptions { ProviderPlan = new ProviderPlanOptions { TimeZoneId = "America/Bogota" } }),
        Mock.Of<ILogger<NotifyProviderPlanExpiringHandler>>());

    /// <summary>
    /// The window of 30 days opens at 00:00 of 5 October in Bogotá and the round of 00:15 raises it.
    /// </summary>
    private static ProviderPlanExpiringDomainEvent Expiring(Instant? occurredAt = null, Instant? validUntil = null, Guid? eventId = null) => new(
        UsageId, "Alanube", 120, 3000, validUntil ?? ContractEnd,
        eventId: eventId ?? Guid.NewGuid(), occurredAt: occurredAt ?? Instant.FromUtc(2027, 10, 5, 5, 15));

    [Fact]
    public async Task HandleAsync_Expiring_NotifiesThePlatformRoleWithoutTenant()
    {
        // Act
        await Handler().HandleAsync(Expiring(), CancellationToken.None);

        // Assert
        var notification = Assert.Single(notifications);
        Assert.Equal(
            (PlatformNotificationKinds.ElectronicInvoicingProviderPlanExpiring, AudienceKind.Role, PlatformRole.ToString(), Guid.Empty.ToString(), ProviderPlanThresholdText.Module, UsageId.ToString()),
            (notification.Kind, notification.Audience.Kind, Assert.Single(notification.Audience.Values), notification.Tenant, notification.Resource.Module, notification.Resource.AggregateId));
    }

    [Fact]
    public async Task HandleAsync_Expiring_SaysTheProviderTheDayInTheZoneOfThePlanAndTheDaysLeft()
    {
        // Act
        await Handler().HandleAsync(Expiring(), CancellationToken.None);

        // Assert: 05:00 UTC of 4 November is 00:00 of that day in Bogotá, not the 3rd nor the 5th.
        Assert.Equal(
            ("El contrato con el proveedor de factura electrónica vence pronto", "El contrato con el proveedor de factura electrónica (Alanube) vence el 4 de noviembre de 2027, en 30 días. Renuévalo para no interrumpir la emisión de documentos electrónicos."),
            (notifications[0].Title, notifications[0].Body));
    }

    [Fact]
    public async Task HandleAsync_OneDayLeft_SaysItInSingular()
    {
        // Act
        await Handler().HandleAsync(Expiring(Instant.FromUtc(2027, 11, 3, 15, 0)), CancellationToken.None);

        // Assert
        Assert.Contains("vence el 4 de noviembre de 2027, en 1 día.", notifications[0].Body);
    }

    [Fact]
    public async Task HandleAsync_EveningInTheZoneOfThePlan_WritesTheDayAndCountsTheDaysInThatZone()
    {
        // Act: 21:00 of 4 October in Bogotá (already the 5th in UTC) and an end at 22:00 of 3 November (the 4th in UTC).
        await Handler().HandleAsync(Expiring(Instant.FromUtc(2027, 10, 5, 2, 0), Instant.FromUtc(2027, 11, 4, 3, 0)), CancellationToken.None);

        // Assert
        Assert.Contains("vence el 3 de noviembre de 2027, en 30 días.", notifications[0].Body);
    }

    [Fact]
    public async Task HandleAsync_Expiring_CarriesTheDatesAndTheDaysInThePayload()
    {
        // Act
        await Handler().HandleAsync(Expiring(), CancellationToken.None);

        // Assert
        Assert.Contains("\"provider\":\"Alanube\",\"validUntil\":\"2027-11-04T05:00:00Z\",\"expiresOn\":\"2027-11-04\",\"daysLeft\":30,\"used\":120,\"documents\":3000", notifications[0].JsonPayload);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_KeepsTheIdsOfTheNoticeAndTheEmail()
    {
        // Arrange
        var data = Expiring();

        // Act
        await Handler().HandleAsync(data, CancellationToken.None);
        await Handler().HandleAsync(data, CancellationToken.None);

        // Assert
        Assert.Equal((notifications[0].Id, emails[0].AggregateId), (notifications[1].Id, emails[1].AggregateId));
    }

    [Fact]
    public async Task HandleAsync_Expiring_EmailsTheOperationsMailboxMarkedAsImportant()
    {
        // Act
        await Handler().HandleAsync(Expiring(), CancellationToken.None);

        // Assert
        var email = Assert.Single(emails);
        Assert.Equal(
            ("Notice", Mailbox, "IMPORTANTE: El contrato con Alanube vence el 4 de noviembre de 2027", Guid.Empty),
            (email.TemplateName, Assert.Single(email.To), email.Variables["subject"], email.Tenant));
    }

    [Fact]
    public async Task HandleAsync_WithoutMailbox_NotifiesOnlyTheInbox()
    {
        // Act
        await Handler(mailbox: "").HandleAsync(Expiring(), CancellationToken.None);

        // Assert
        Assert.Equal((1, 0), (notifications.Count, emails.Count));
    }

    [Fact]
    public async Task HandleAsync_WithoutRole_FailsSoTheMessageIsRetried()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => Handler(role: Guid.Empty).HandleAsync(Expiring(), CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
