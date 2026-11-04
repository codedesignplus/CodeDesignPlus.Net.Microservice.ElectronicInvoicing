using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessDueElectronicDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessProviderEvent;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

public class ProcessDueElectronicDocumentsCommandHandlerTest
{
    [Fact]
    public async Task Handle_OneDocumentFails_TriesTheRestAndThrowsAtTheEnd()
    {
        // Arrange
        var repository = new Mock<IElectronicDocumentRepository>();
        var mediator = new Mock<IMediator>();
        var failing = TestData.Document();
        var fine = TestData.Document();

        repository.Setup(x => x.FindDueAsync(TestData.Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([failing, fine]);
        mediator.Setup(x => x.Send(It.Is<ProcessElectronicDocumentCommand>(c => c.Id == failing.Id), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        mediator.Setup(x => x.Send(It.Is<ProcessElectronicDocumentCommand>(c => c.Id == fine.Id), It.IsAny<CancellationToken>())).ReturnsAsync(ProcessOutcome.Validated);

        var handler = new ProcessDueElectronicDocumentsCommandHandler(repository.Object, mediator.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), NullLogger<ProcessDueElectronicDocumentsCommandHandler>.Instance);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ProcessDueElectronicDocumentsCommand(), CancellationToken.None));

        // Assert
        mediator.Verify(x => x.Send(It.Is<ProcessElectronicDocumentCommand>(c => c.Id == fine.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class RetryElectronicDocumentCommandHandlerTest
{
    private readonly Mock<IElectronicDocumentRepository> repository = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IDocumentContentResolver> contents = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly RetryElectronicDocumentCommandHandler handler;

    public RetryElectronicDocumentCommandHandlerTest()
    {
        var issuer = TestData.EnabledIssuer();

        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        contents.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<DocumentContentInput>(), It.IsAny<ProviderCapabilities>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.InvoiceContent("900000009"));
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();

        handler = new RetryElectronicDocumentCommandHandler(repository.Object, issuers.Object, factory.Object, contents.Object, user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));
    }

    private ElectronicDocumentAggregate Stored(ElectronicDocumentAggregate document)
    {
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        return document;
    }

    private static ElectronicDocumentAggregate Rejected()
    {
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 1), Guid.NewGuid());
        document.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));
        document.Reject(RejectionOrigin.Dian, ["rejected"], "provider-1", "Rechazada", TestData.Now);
        document.GetAndClearEvents();

        return document;
    }

    private static DocumentContentInput Input() => new(TestData.Buyer("900000009"), [TestData.Line()], new DocumentTotalsInput("COP", 2_000_000, 0, 380_000, 0, 0, 2_380_000), [], new PaymentMeans("1", "10"), new LocalDate(2026, 11, 30), [], null, null);

    [Fact]
    public async Task Handle_RejectedWithContent_CorrectsItAndQueuesIt()
    {
        // Arrange
        var document = Stored(Rejected());

        // Act
        await handler.Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None);

        // Assert
        Assert.Equal((ElectronicDocumentStatus.Draft, 1), (document.Status, pubsub.Published().OfType<ElectronicDocumentQueuedDomainEvent>().Count()));
    }

    [Fact]
    public async Task Handle_RejectedWithoutContent_ThrowsContentIsRequired()
    {
        // Arrange
        var document = Stored(Rejected());

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new RetryElectronicDocumentCommand(document.Id, null, null), CancellationToken.None));

        // Assert
        Assert.Equal(DomainErrors.ContentIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_Stalled_RestartsItAndQueuesIt()
    {
        // Arrange
        var document = TestData.Document();

        document.Stall(TestData.Now);
        Stored(document);

        // Act
        await handler.Handle(new RetryElectronicDocumentCommand(document.Id, null, null), CancellationToken.None);

        // Assert
        Assert.Equal(((Instant?)null, 1), (document.StalledAt, pubsub.Published().OfType<ElectronicDocumentQueuedDomainEvent>().Count()));
    }
}

public class ProcessProviderEventCommandHandlerTest
{
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IProviderEventReader> reader = new();
    private readonly Mock<IElectronicDocumentRepository> repository = new();
    private readonly Mock<IMediator> mediator = new();
    private readonly ElectronicDocumentAggregate document = TestData.Document();
    private readonly ProcessProviderEventCommandHandler handler;

    public ProcessProviderEventCommandHandlerTest()
    {
        factory.Setup(x => x.FindEvents("aliaddo")).Returns(reader.Object);
        reader.Setup(x => x.Capabilities).Returns(Fakes.Capabilities(updates: StatusUpdatesMode.Push));
        reader.Setup(x => x.IsAuthentic(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>())).Returns(true);
        reader.Setup(x => x.ReadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderDocumentEvent("provider-1", "Aprobada"));
        repository.Setup(x => x.FindByProviderDocumentIdAsync("provider-1", It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();

        handler = new ProcessProviderEventCommandHandler(factory.Object, repository.Object, mediator.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), NullLogger<ProcessProviderEventCommandHandler>.Instance);
    }

    private Task<bool> Receive() => handler.Handle(new ProcessProviderEventCommand("aliaddo", "{}", new Dictionary<string, string>()), CancellationToken.None);

    [Fact]
    public async Task Handle_AuthenticCall_LooksAtTheDocumentNow()
    {
        // Act
        var handled = await Receive();

        // Assert
        mediator.Verify(x => x.Send(It.Is<ProcessElectronicDocumentCommand>(c => c.Id == document.Id), It.IsAny<CancellationToken>()), Times.Once);

        Assert.True(handled);
    }

    [Fact]
    public async Task Handle_SignatureThatDoesNotMatch_IsDiscarded()
    {
        // Arrange
        reader.Setup(x => x.IsAuthentic(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>())).Returns(false);

        // Act
        var handled = await Receive();

        // Assert
        mediator.Verify(x => x.Send(It.IsAny<ProcessElectronicDocumentCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.False(handled);
    }

    [Fact]
    public async Task Handle_UpdatesAskedFor_IgnoresTheCall()
    {
        // Arrange
        reader.Setup(x => x.Capabilities).Returns(Fakes.Capabilities(updates: StatusUpdatesMode.Poll));

        // Act
        var handled = await Receive();

        // Assert
        reader.Verify(x => x.IsAuthentic(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);

        Assert.False(handled);
    }
}
