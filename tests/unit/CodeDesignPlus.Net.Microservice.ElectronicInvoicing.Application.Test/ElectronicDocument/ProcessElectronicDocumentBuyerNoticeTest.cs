using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using StoredFile = CodeDesignPlus.Net.ValueObjects.FileServer.File;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

/// <summary>
/// A provider that leaves the delivery to the buyer to this service writes the email, and the round asks ms-emails for it
/// once the document is validated and its files are kept; a provider that delivers by itself gets nothing asked.
/// </summary>
public class ProcessElectronicDocumentBuyerNoticeTest
{
    private static readonly DocumentNumber Number = new("SETP", 990000000);

    private static readonly BuyerNotice Notice = new("comprador@example.com", "Documento de prueba sin validez fiscal: Factura SETP990000000", "Documento de prueba SETP990000000", "<p>Hola</p>");

    private readonly StoredFile xml = StoredFile.Create(Guid.Parse("cc000000-0000-4000-8000-000000000001"), "electronic-documents", "SETP990000000.xml", ".xml", "application/xml", 3);
    private readonly StoredFile pdf = StoredFile.Create(Guid.Parse("cc000000-0000-4000-8000-000000000002"), "electronic-documents", "SETP990000000.pdf", ".pdf", "application/pdf", 4);

    private readonly Mock<IElectronicDocumentRepository> repository = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IBuyerNoticeProvider> notices;
    private readonly Mock<IProviderIssuerResolver> resolver = new();
    private readonly Mock<IDocumentFileStore> files = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly ElectronicDocumentAggregate document;
    private readonly ElectronicInvoicingOptions settings = Fakes.Options();
    private readonly ProcessElectronicDocumentCommandHandler handler;

    public ProcessElectronicDocumentBuyerNoticeTest()
    {
        document = TestData.Document();
        document.AssignNumber(Number, Guid.NewGuid());

        notices = provider.As<IBuyerNoticeProvider>();
        notices.Setup(x => x.GetBuyerNoticeAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(Notice);

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Find("Simulated")).Returns(provider.Object);
        resolver.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerAggregate x, CancellationToken _) => ProviderIssuer.From(x, new Dictionary<string, string>()));
        files.Setup(x => x.StoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentFiles(xml, null, pdf));
        provider.Setup(x => x.GetFilesAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProviderFiles.None);

        Answers(ProviderDocumentStatus.Accepted, "cufe-1");

        handler = Handler(factory.Object);
    }

    private ProcessElectronicDocumentCommandHandler Handler(IElectronicInvoiceProviderFactory providers) => new(repository.Object, issuers.Object, providers, resolver.Object, new Mock<INumberingService>().Object, files.Object, pubsub.Object, new RecordingAvailability(), Fakes.Clock(), Fakes.Options(settings), NullLogger<ProcessElectronicDocumentCommandHandler>.Instance);

    private void Answers(ProviderDocumentStatus status, string? cufe = null)
    {
        var state = new ProviderDocumentState(status, "provider-1", cufe, Number, new LocalDate(2026, 11, 4), status == ProviderDocumentStatus.Rejected ? ["Rechazo simulado."] : [], RejectionOrigin.Dian, status.ToString());

        provider.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderSubmission("provider-1", state));
        provider.Setup(x => x.GetStateAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(state);
    }

    private Task<ProcessOutcome> Run() => handler.Handle(new ProcessElectronicDocumentCommand(document.Id), CancellationToken.None);

    private List<SendEmailDomainEvent> Emails() => [.. pubsub.Published().OfType<SendEmailDomainEvent>()];

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_AsksForOneToTheBuyer()
    {
        // Act
        await Run();

        // Assert
        Assert.Equal(["comprador@example.com"], Emails().Single().To);
    }

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_AttachesThePdfAndTheXmlOfTheDocument()
    {
        // Act
        await Run();

        // Assert: as ms-emails reads them, by id, name and folder.
        Assert.Equal([new EmailAttachment(pdf.Id, "SETP990000000.pdf", "electronic-documents"), new EmailAttachment(xml.Id, "SETP990000000.xml", "electronic-documents")], Emails().Single().Attachments);
    }

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_SendsItWithTheConfiguredTemplateAsTheTenantOfTheDocument()
    {
        // Arrange
        settings.BuyerNoticeEmailTemplate = "BuyerNotice";

        // Act
        await Run();

        // Assert
        Assert.Equal(("BuyerNotice", TestData.Tenant, 0, 0), (Emails().Single().TemplateName, Emails().Single().Tenant, Emails().Single().Cc.Count, Emails().Single().Bcc.Count));
    }

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_FillsTheVariablesOfTheTemplate()
    {
        // Act
        await Run();

        // Assert: the year is the one of the issuer's zone at the fixed clock.
        Assert.Equal(new Dictionary<string, string> { ["subject"] = Notice.Subject, ["title"] = Notice.Title, ["content"] = Notice.Content, ["organization_name"] = "Emisor de Prueba SAS", ["current_year"] = "2026" }, Emails().Single().Variables);
    }

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_AsksForItByTheDocumentInTheProvider()
    {
        // Act
        await Run();

        // Assert
        notices.Verify(x => x.GetBuyerNoticeAsync(It.IsAny<ProviderIssuer>(), It.Is<ProviderDocumentLookup>(l => l.ProviderDocumentId == "provider-1" && l.Number == Number), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProviderWritesNoEmail_AsksForNone()
    {
        // Arrange: the buyer has no email, or the provider is told not to send.
        notices.Setup(x => x.GetBuyerNoticeAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync((BuyerNotice?)null);

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, 0), (outcome, Emails().Count));
    }

    [Fact]
    public async Task Handle_EmailWithoutAnAddress_AsksForNone()
    {
        // Arrange
        notices.Setup(x => x.GetBuyerNoticeAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(Notice with { To = " " });

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, 0), (outcome, Emails().Count));
    }

    [Fact]
    public async Task Handle_ProviderThatDeliversByItself_AsksForNoEmail()
    {
        // Arrange: an adapter without the capability, as the real ones.
        var real = new Mock<IElectronicInvoiceProvider>();
        var state = new ProviderDocumentState(ProviderDocumentStatus.Accepted, "provider-1", "cufe-1", Number, new LocalDate(2026, 11, 4), [], null, "Accepted");

        real.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        real.Setup(x => x.SubmitAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderSubmission("provider-1", state));
        real.Setup(x => x.GetFilesAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProviderFiles.None);

        var providers = new Mock<IElectronicInvoiceProviderFactory>();

        providers.Setup(x => x.Find("Simulated")).Returns(real.Object);

        // Act
        var outcome = await Handler(providers.Object).Handle(new ProcessElectronicDocumentCommand(document.Id), CancellationToken.None);

        // Assert
        Assert.Equal((ProcessOutcome.Validated, 0), (outcome, Emails().Count));
    }

    [Fact]
    public async Task Handle_ProviderRejects_AsksForNoEmail()
    {
        // Arrange
        Answers(ProviderDocumentStatus.Rejected);

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Rejected, 0), (outcome, Emails().Count));
    }

    [Fact]
    public async Task Handle_FilesCannotBeStored_ValidatesAndWaitsForThemBeforeTheEmail()
    {
        // Arrange
        files.Setup(x => x.StoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("filestorage down"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, 0), (outcome, Emails().Count));
    }

    [Fact]
    public async Task Handle_FilesKeptAfterTheValidation_AsksForTheEmailThen()
    {
        // Arrange: validated without its files, and due for them.
        document.Validate("provider-1", "cufe-1", null, new LocalDate(2026, 11, 4), "Accepted", null, TestData.Now);
        document.GetAndClearEvents();

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.FilesKept, 2), (outcome, Emails().Single().Attachments.Count));
    }

    [Fact]
    public async Task Handle_FilesStillMissing_AsksForNoEmail()
    {
        // Arrange
        document.Validate("provider-1", "cufe-1", null, new LocalDate(2026, 11, 4), "Accepted", null, TestData.Now);
        document.GetAndClearEvents();
        files.Setup(x => x.StoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderFiles>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentFiles(null, null, pdf));

        // Act
        await Run();

        // Assert
        Assert.Empty(Emails());
    }

    [Fact]
    public async Task Handle_EmailCannotBeWritten_ValidatesAnyway()
    {
        // Arrange
        notices.Setup(x => x.GetBuyerNoticeAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ProviderDocumentLookup>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no texts"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, ElectronicDocumentStatus.Validated), (outcome, document.Status));
    }

    [Fact]
    public async Task Handle_EmailCannotBePublished_ValidatesAnyway()
    {
        // Arrange: the bus refuses the email, not the events of the document.
        pubsub.Setup(x => x.PublishAsync(It.IsAny<SendEmailDomainEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("bus down"));

        // Act
        var outcome = await Run();

        // Assert
        Assert.Equal((ProcessOutcome.Validated, ElectronicDocumentStatus.Validated), (outcome, document.Status));
    }

    [Fact]
    public async Task Handle_ProviderWritesTheBuyerEmail_AnnouncesTheValidationBeforeTheEmail()
    {
        // Act
        await Run();

        var published = pubsub.Published();

        // Assert
        Assert.True(published.FindIndex(x => x is ElectronicDocumentValidatedDomainEvent) < published.FindIndex(x => x is SendEmailDomainEvent));
    }
}
