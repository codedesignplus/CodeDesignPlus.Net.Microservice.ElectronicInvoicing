using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

public class IssueElectronicDocumentCommandHandlerTest
{
    private readonly Mock<IElectronicDocumentRepository> repository = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IDocumentContentResolver> contents = new();
    private readonly Mock<INumberingService> numbering = new();
    private readonly Mock<IQuotaGate> quotas = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly NumberingRangeAggregate range;
    private readonly IssueElectronicDocumentCommandHandler handler;

    public IssueElectronicDocumentCommandHandlerTest()
    {
        range = TestData.Range(issuer);

        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Code).Returns("Simulated");
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        contents.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<DocumentContentInput>(), It.IsAny<ProviderCapabilities>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.InvoiceContent());
        numbering.Setup(x => x.SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(range);
        numbering.Setup(x => x.ReserveAsync(range.Id, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentNumber("SETP", 990000000));
        quotas.Setup(x => x.ReserveAsync(TestData.Tenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();

        handler = new IssueElectronicDocumentCommandHandler(repository.Object, issuers.Object, factory.Object, contents.Object, numbering.Object, quotas.Object, user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), NullLogger<IssueElectronicDocumentCommandHandler>.Instance);
    }

    private static IssueElectronicDocumentCommand Command(Guid? id = null, string reference = "orders:order:1") => new(id ?? Guid.NewGuid(), null, false, ElectronicDocumentType.Invoice, reference, new DocumentContentInput(TestData.Buyer(), [TestData.Line()], new DocumentTotalsInput("COP", 2_000_000, 0, 380_000, 0, 0, 2_380_000), [], new PaymentMeans("1", "10"), new LocalDate(2026, 11, 30), [], null, null), null);

    [Fact]
    public async Task Handle_NewDocument_SavesItCountsTheQuotaAndTakesItsNumber()
    {
        // Arrange
        var command = Command();
        ElectronicDocumentAggregate? saved = null;

        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).Callback<ElectronicDocumentAggregate, CancellationToken>((document, _) => saved = document).ReturnsAsync(true);
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => saved!);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        quotas.Verify(x => x.ReserveAsync(TestData.Tenant, ElectronicDocumentType.Invoice, TestData.Now, It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal((ElectronicDocumentStatus.Draft, "SETP", (long?)990000000), (result.Status, result.Prefix, result.Consecutive));
    }

    [Fact]
    public async Task Handle_NewDocument_PublishesTheQueuedEventAndTheQuotaAlerts()
    {
        // Arrange
        var command = Command();
        var alert = new DocumentQuotaThresholdReachedDomainEvent(Guid.NewGuid(), TestData.Tenant, 80, 8, 10, TestData.Now);

        quotas.Setup(x => x.ReserveAsync(TestData.Tenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ReturnsAsync([alert]);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var published = pubsub.Published();

        Assert.Equal((1, true), (published.OfType<ElectronicDocumentQueuedDomainEvent>().Count(x => x.AggregateId == command.Id), published.Contains(alert)));
    }

    [Fact]
    public async Task Handle_SameIdAndSameContent_ReturnsTheDocumentWithoutCountingItAgain()
    {
        // Arrange
        var command = Command();
        var existing = TestData.Document(command.Id);

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        quotas.Verify(x => x.ReserveAsync(It.IsAny<Guid>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task Handle_SameIdAndAnotherContent_ThrowsDocumentIdReusedWithOtherContent()
    {
        // Arrange
        var command = Command(reference: "orders:order:2");

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync(TestData.Document(command.Id));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(command, CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.DocumentIdReusedWithOtherContent.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_TenantWithoutIssuer_ThrowsIssuerNotFound()
    {
        // Arrange
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync((IssuerAggregate)null!);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Command(), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.IssuerNotFound.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_IssuerWithoutMandate_ThrowsMandateNotAccepted()
    {
        // Arrange
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(TestData.RegisteredIssuer());

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Command(), CancellationToken.None));

        // Assert
        Assert.Equal(DomainErrors.MandateNotAccepted.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_SuspendedIssuer_ThrowsIssuerNotEnabled()
    {
        // Arrange
        var suspended = TestData.EnabledIssuer();

        suspended.Suspend("215", TestData.User);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suspended);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Command(), CancellationToken.None));

        // Assert
        Assert.Equal(DomainErrors.IssuerNotEnabled.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_QuotaExhausted_SavesNothing()
    {
        // Arrange
        quotas.Setup(x => x.ReserveAsync(TestData.Tenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Domain, DomainErrors.QuotaExhausted.GetCode(), "quota"));

        // Act
        await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Command(), CancellationToken.None));

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AnotherRequestSavedItFirst_GivesBackTheQuotaAndPublishesNothing()
    {
        // Arrange
        var command = Command();
        var winner = TestData.Document(command.Id);

        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.SetupSequence(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ElectronicDocumentAggregate)null!).ReturnsAsync(winner);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        quotas.Verify(x => x.ReleaseAsync(TestData.Tenant, TestData.Now, It.IsAny<CancellationToken>()), Times.Once);

        Assert.Empty(pubsub.Published());
    }

    [Fact]
    public async Task Handle_PlatformIssuer_IssuesForThePlatformTenantWhateverTheUser()
    {
        // Arrange
        var platform = TestData.EnabledIssuer(tenant: TestData.PlatformTenant);
        var command = Command() with { PlatformIssuer = true };

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(platform.Id, It.IsAny<CancellationToken>())).ReturnsAsync(platform);
        numbering.Setup(x => x.SelectAsync(TestData.PlatformTenant, ElectronicDocumentType.Invoice, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(range);
        quotas.Setup(x => x.ReserveAsync(TestData.PlatformTenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.Is<ElectronicDocumentAggregate>(document => document.Tenant == TestData.PlatformTenant), It.IsAny<CancellationToken>()), Times.Once);
    }
}
