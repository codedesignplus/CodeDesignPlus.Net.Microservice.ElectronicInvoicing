using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

public class ProcessElectronicDocumentCommandHandlerTest
{
    private static readonly DocumentNumber Number = new("SETP", 990000000);

    private readonly Mock<IElectronicDocumentRepository> repository = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IProviderIssuerResolver> resolver = new();
    private readonly Mock<INumberingService> numbering = new();
    private readonly Mock<IDocumentFileStore> files = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly ElectronicDocumentAggregate document;
    private readonly ElectronicInvoicingOptions settings = Fakes.Options();
    private readonly ProcessElectronicDocumentCommandHandler handler;

    public ProcessElectronicDocumentCommandHandlerTest()
    {
        document = TestData.Document();
        document.AssignNumber(Number, Guid.NewGuid());

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Find("Simulated")).Returns(provider.Object);
        resolver.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerAggregate x, CancellationToken _) => ProviderIssuer.From(x, new Dictionary<string, string>()));
        files.Setup(x => x.StoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentFiles(null, null, null));
        provider.Setup(x => x.GetFilesAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProviderFiles.None);

        handler = new ProcessElectronicDocumentCommandHandler(repository.Object, issuers.Object, factory.Object, resolver.Object, numbering.Object, files.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(settings), NullLogger<ProcessElectronicDocumentCommandHandler>.Instance);
    }

    private void Answers(ProviderDocumentStatus status, string? cufe = null, IReadOnlyList<string>? reasons = null)
    {
        var state = new ProviderDocumentState(status, "provider-1", cufe, Number, new LocalDate(2026, 11, 4), reasons ?? [], RejectionOrigin.Dian, status.ToString());

        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderSubmission("provider-1", state));
        provider.Setup(x => x.GetStateAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(state);
    }

    private Task<ProcessOutcome> Run() => handler.Handle(new ProcessElectronicDocumentCommand(document.Id), CancellationToken.None);

    [Fact]
    public async Task Handle_ProviderAccepts_ValidatesAndAnnouncesIt()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Accepted, "cufe-1");

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, ElectronicDocumentStatus.Validated, 1), (outcome, document.Status, pubsub.Published().OfType<ElectronicDocumentValidatedDomainEvent>().Count()));
    }

    [Fact]
    public async Task Handle_ProviderAccepts_StoresTheFilesOfTheDocument()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Accepted, "cufe-1");

        // Act
        await Run();

        // Assert
        files.Verify(x => x.StoreAsync(TestData.Tenant, document.Id, It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FilesCannotBeStored_ValidatesAnyway()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Accepted, "cufe-1");
        files.Setup(x => x.StoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("filestorage down"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal(ProcessOutcome.Validated, outcome);
    }

    [Fact]
    public async Task Handle_TaxAuthorityRejects_RejectsWithTheReasons()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Rejected, reasons: ["Regla FAU04: el total no corresponde."]);

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Rejected, "Regla FAU04: el total no corresponde."), (outcome, document.Rejection!.Reasons.Single()));
    }

    [Fact]
    public async Task Handle_TaxAuthorityDoesNotAnswer_StaysSentInContingencyAndIsNotRejected()
    {
        // Arrange
        Answers(ProviderDocumentStatus.DianUnavailable);

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Waiting, ElectronicDocumentStatus.Sent, true, (Instant?)(TestData.Now + Duration.FromMinutes(1))), (outcome, document.Status, document.Contingency, document.NextCheckAt));
    }

    [Fact]
    public async Task Handle_ProviderDoesNotAnswer_KeepsTheDraftWithItsBackoff()
    {
        // Arrange
        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "unavailable"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Deferred, ElectronicDocumentStatus.Draft, 1, "unavailable"), (outcome, document.Status, document.Attempts, document.LastCheckOutcome));
    }

    [Fact]
    public async Task Handle_PrecedingDocumentNotValidated_WaitsWithoutSending()
    {
        // Arrange
        var preceding = TestData.Document(type: ElectronicDocumentType.CreditNote);
        var follower = TestData.Document(precededBy: preceding.Id);

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(follower.Id, It.IsAny<CancellationToken>())).ReturnsAsync(follower);
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(preceding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(preceding);

        // Act
        var outcome = await handler.Handle(new ProcessElectronicDocumentCommand(follower.Id), CancellationToken.None);

        // Assert
        provider.Verify(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal((ProcessOutcome.Waiting, "waiting-preceding"), (outcome, follower.LastCheckOutcome));
    }

    [Fact]
    public async Task Handle_PreviousRoundMayHaveArrived_AdoptsWhatTheProviderHasInsteadOfSendingTwice()
    {
        // Arrange: a first round that did not get its answer.
        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "unavailable"));

        await Run();

        document.Expedite(TestData.Now, TestData.User);
        Answers(ProviderDocumentStatus.Accepted, "cufe-1");

        // Act
        var outcome = await Run();

        // Assert
        provider.Verify(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(ProcessOutcome.Validated, outcome);
    }

    [Fact]
    public async Task Handle_LookupFindsARejection_SendsTheCorrectionAnyway()
    {
        // Arrange: the round before failed; the provider still has the rejection of before the correction.
        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "unavailable"));

        await Run();

        document.Expedite(TestData.Now, TestData.User);

        var rejected = new ProviderDocumentState(ProviderDocumentStatus.Rejected, "old", null, Number, null, ["old rejection"], RejectionOrigin.Dian, "Rechazada");
        var accepted = new ProviderDocumentState(ProviderDocumentStatus.Accepted, "provider-2", "cufe-2", Number, null, [], null, "Aprobada");

        provider.Setup(x => x.GetStateAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(rejected);
        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderSubmission("provider-2", accepted));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, "cufe-2"), (outcome, document.Cufe));
    }

    [Fact]
    public async Task Handle_AnotherRoundHoldsTheDocument_DoesNotTalkToTheProvider()
    {
        // Arrange
        document.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));

        // Act
        var outcome = await Run();

        // Assert
        provider.Verify(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(ProcessOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task Handle_LastAttemptWithoutAnswer_StallsAndAnnouncesIt()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Processing);
        settings.StatusCheck.MaxAttempts = 1;

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Stalled, 1), (outcome, pubsub.Published().OfType<ElectronicDocumentStalledDomainEvent>().Count()));
    }

    [Fact]
    public async Task Handle_CredentialsRejected_CountsTheRejectionOnTheIssuer()
    {
        // Arrange
        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "302", "rejected"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Deferred, 1), (outcome, issuer.ConsecutiveCredentialRejections));
    }

    [Fact]
    public async Task Handle_DraftWithoutNumber_TakesItBeforeSending()
    {
        // Arrange
        var unnumbered = TestData.Document();
        var range = TestData.Range(issuer);

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(unnumbered.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unnumbered);
        numbering.Setup(x => x.SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(range);
        numbering.Setup(x => x.ReserveAsync(range.Id, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(Number);
        Answers(ProviderDocumentStatus.Processing);

        // Act
        await handler.Handle(new ProcessElectronicDocumentCommand(unnumbered.Id), CancellationToken.None);

        // Assert
        provider.Verify(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.Is<ProviderDocument>(sent => sent.Number == Number), It.IsAny<CancellationToken>()), Times.Once);
    }
}
