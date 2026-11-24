using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.gRpc.Clients.Services.Notification;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.PubSub.Abstractions;
using CodeDesignPlus.Net.ValueObjects.Financial;
using NodaTime;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Test.Consumers;

/// <summary>
/// The notice of a threshold of the plan with the provider (Kappali pendings/744): it goes to the inbox of the role of the
/// platform, without tenant, says the month, the percentage and the documents used and, from 100 %, the estimated cost of
/// the overage; with an operations mailbox it is also emailed.
/// </summary>
public class NotifyProviderPlanThresholdHandlerTest
{
    private static readonly Guid PlatformRole = Guid.Parse("20000000-0000-0000-0000-000000000010");
    private static readonly Guid UsageId = Guid.Parse("aa000000-0000-4000-8000-000000000744");
    private static readonly Currency Cop = Currency.Create(Guid.Parse("ee000000-0000-4000-8000-000000000105"), "Peso Colombiano", "COP", "$", 2, 170);
    private const string Mailbox = "operations@example.com";

    private readonly Mock<IInboxGrpc> inbox = new();
    private readonly Mock<IPubSub> pubSub = new();
    private readonly Mock<ICurrencyGrpc> currencies = new();
    private readonly List<NotificationRequest> notifications = [];
    private readonly List<SendEmailDomainEvent> emails = [];

    public NotifyProviderPlanThresholdHandlerTest()
    {
        inbox.Setup(x => x.NotifyAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => notifications.Add(request))
            .Returns(Task.CompletedTask);
        pubSub.Setup(x => x.PublishAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((@event, _) => emails.Add((SendEmailDomainEvent)@event))
            .Returns(Task.CompletedTask);
        currencies.Setup(x => x.GetCurrencyAsync(It.IsAny<Guid?>(), "COP", It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cop);
    }

    private NotifyProviderPlanThresholdHandler Handler(Guid? role = null, string mailbox = Mailbox) => new(
        inbox.Object, pubSub.Object, currencies.Object,
        MsOptions.Create(new PlatformNoticeOptions { PlatformAdministratorRole = role ?? PlatformRole, OperationsMailbox = mailbox, EmailTemplate = "Notice" }),
        Mock.Of<ILogger<NotifyProviderPlanThresholdHandler>>());

    private static ProviderPlanThresholdReachedDomainEvent Reached(int threshold, long used, long overage = 0, long cost = 0, Guid? eventId = null) => new(
        UsageId, "Alanube", threshold, used, 3000, Instant.FromUtc(2026, 12, 1, 5, 0), new LocalDate(2026, 11, 1), overage, cost, "COP",
        eventId: eventId ?? Guid.NewGuid(), occurredAt: Instant.FromUtc(2026, 11, 24, 15, 0));

    [Fact]
    public async Task HandleAsync_Threshold_NotifiesThePlatformRoleWithoutTenant()
    {
        // Act
        await Handler().HandleAsync(Reached(70, 2100), CancellationToken.None);

        // Assert
        var notification = Assert.Single(notifications);
        Assert.Equal(
            (PlatformNotificationKinds.ElectronicInvoicingProviderPlanThreshold, AudienceKind.Role, PlatformRole.ToString(), Guid.Empty.ToString(), ProviderPlanThresholdText.Module, UsageId.ToString()),
            (notification.Kind, notification.Audience.Kind, Assert.Single(notification.Audience.Values), notification.Tenant, notification.Resource.Module, notification.Resource.AggregateId));
    }

    [Fact]
    public async Task HandleAsync_BelowTheIncluded_SaysTheMonthThePercentageAndTheDocumentsUsed()
    {
        // Act
        await Handler().HandleAsync(Reached(90, 2700), CancellationToken.None);

        // Assert
        Assert.Equal(
            ("Bolsa de documentos electrónicos al 90 %", "La bolsa de documentos electrónicos de noviembre de 2026 va en 90 % (usados 2.700 de 3.000). Los documentos que pasen de 3.000 en el mes se cobran como excedente."),
            (notifications[0].Title, notifications[0].Body));
    }

    [Fact]
    public async Task HandleAsync_RoundCountedManyDocuments_SaysTheRealPercentageAndNotTheThreshold()
    {
        // Act: the notice of 70 % arrives when the month is already at 95 %.
        await Handler().HandleAsync(Reached(70, 2850), CancellationToken.None);

        // Assert
        Assert.StartsWith("La bolsa de documentos electrónicos de noviembre de 2026 va en 95 % (usados 2.850 de 3.000).", notifications[0].Body);
    }

    [Fact]
    public async Task HandleAsync_Exhausted_SaysThatEveryDocumentIsBilledAsOverageWithTheEstimatedCost()
    {
        // Act: 30 documents over the included ones at COP 14 each.
        await Handler().HandleAsync(Reached(100, 3030, overage: 30, cost: 42000), CancellationToken.None);

        // Assert
        Assert.Equal(
            ("Bolsa de documentos electrónicos agotada", "La bolsa de documentos electrónicos de noviembre de 2026 va en 101 % (usados 3.030 de 3.000). Desde aquí cada documento se cobra como excedente: van 30 documentos de excedente, con un costo estimado acumulado de $ 420,00."),
            (notifications[0].Title, notifications[0].Body));
    }

    [Fact]
    public async Task HandleAsync_Threshold_CarriesTheUnformattedAmountsInThePayload()
    {
        // Act
        await Handler().HandleAsync(Reached(100, 3030, overage: 30, cost: 42000), CancellationToken.None);

        // Assert
        Assert.Contains("\"month\":\"2026-11-01\",\"provider\":\"Alanube\",\"threshold\":100,\"percentage\":101,\"used\":3030,\"documents\":3000,\"overage\":30,\"estimatedOverageCost\":42000,\"currency\":\"COP\"", notifications[0].JsonPayload);
    }

    [Fact]
    public async Task HandleAsync_BelowTheIncluded_DoesNotReadTheCurrency()
    {
        // Act
        await Handler().HandleAsync(Reached(70, 2100), CancellationToken.None);

        // Assert
        currencies.Verify(x => x.GetCurrencyAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_KeepsTheIdsOfTheNoticeAndTheEmail()
    {
        // Arrange
        var data = Reached(90, 2700);

        // Act
        await Handler().HandleAsync(data, CancellationToken.None);
        await Handler().HandleAsync(data, CancellationToken.None);

        // Assert
        Assert.Equal((notifications[0].Id, emails[0].AggregateId), (notifications[1].Id, emails[1].AggregateId));
    }

    [Fact]
    public async Task HandleAsync_Exhausted_EmailsTheOperationsMailboxMarkedAsImportant()
    {
        // Act
        await Handler().HandleAsync(Reached(100, 3030, overage: 30, cost: 42000), CancellationToken.None);

        // Assert
        var email = Assert.Single(emails);
        Assert.Equal(
            ("Notice", Mailbox, "IMPORTANTE: Bolsa de documentos electrónicos de noviembre de 2026 al 100 %", Guid.Empty),
            (email.TemplateName, Assert.Single(email.To), email.Variables["subject"], email.Tenant));
    }

    [Fact]
    public async Task HandleAsync_WithoutMailbox_NotifiesOnlyTheInbox()
    {
        // Act
        await Handler(mailbox: "").HandleAsync(Reached(90, 2700), CancellationToken.None);

        // Assert
        Assert.Equal((1, 0), (notifications.Count, emails.Count));
    }

    [Fact]
    public async Task HandleAsync_WithoutRole_FailsSoTheMessageIsRetried()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => Handler(role: Guid.Empty).HandleAsync(Reached(90, 2700), CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
