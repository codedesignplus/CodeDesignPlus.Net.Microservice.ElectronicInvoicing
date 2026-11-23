using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

/// <summary>
/// An issuer kept with a tax id of a type that carries a check digit, without the digit that belongs to it: it was saved
/// before the rule, and it issues nothing until its fiscal data are corrected.
/// </summary>
public class StoredIssuerTaxIdTest
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

    public StoredIssuerTaxIdTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Code).Returns("Simulated");
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        contents.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<DocumentContentInput>(), It.IsAny<ProviderCapabilities>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.InvoiceContent());
        numbering.Setup(x => x.ReserveAsync(It.IsAny<Guid>(), It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentNumber("SETP", 990000000));
        quotas.Setup(x => x.ReserveAsync(TestData.Tenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();
    }

    /// <summary>
    /// An enabled issuer as it was saved before the rule: the tax id went through no check digit.
    /// </summary>
    private IssuerAggregate StoredIssuer(string taxId, string typeCode = "31")
    {
        var issuer = IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData() with { TaxIdTypeCode = typeCode, TaxId = taxId }, "Simulated", TestData.User, CheckDigitRules.None);

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);
        issuer.Enable(IssuerEnvironment.Test, false, TestData.User);
        issuer.GetAndClearEvents();

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        numbering.Setup(x => x.SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.Range(issuer));

        return issuer;
    }

    private IssueElectronicDocumentCommandHandler IssueHandler(ElectronicInvoicingOptions? options = null) => new(repository.Object, issuers.Object, factory.Object, contents.Object, numbering.Object, quotas.Object, Mock.Of<ITestProviderGrantRepository>(), user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(options ?? Fakes.Options()), NullLogger<IssueElectronicDocumentCommandHandler>.Instance);

    private RetryElectronicDocumentCommandHandler RetryHandler() => new(repository.Object, issuers.Object, factory.Object, contents.Object, user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));

    private static DocumentContentInput Input() => new(TestData.Buyer(), [TestData.Line()], new DocumentTotalsInput("COP", 2_000_000, 0, 380_000, 0, 0, 2_380_000), [], new PaymentMeans("1", "10"), new LocalDate(2026, 11, 30), [], null, null);

    private static IssueElectronicDocumentCommand Issue(Guid? id = null) => new(id ?? Guid.NewGuid(), null, false, ElectronicDocumentType.Invoice, "orders:order:1", Input(), null);

    private ElectronicDocumentAggregate StoredRejected()
    {
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 1), Guid.NewGuid());
        document.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));
        document.Reject(RejectionOrigin.Dian, ["rejected"], "provider-1", "Rechazada", TestData.Now);
        document.GetAndClearEvents();

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        return document;
    }

    [Theory]
    [InlineData("901777333")]
    [InlineData("9018887773")]
    [InlineData("901777333-5")]
    public async Task Issue_IssuerKeptWithoutItsRightCheckDigit_IsRefusedSayingWhyBeforeAnythingIsResolved(string taxId)
    {
        // Arrange
        StoredIssuer(taxId);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => IssueHandler().Handle(Issue(), CancellationToken.None));

        // Assert
        Assert.Equal((DomainErrors.IssuerTaxIdWithoutItsCheckDigit.GetCode(), Layer.Domain), (exception.Code, exception.Layer));
        Assert.Contains(taxId, exception.Error!.Value.GetMessage("es"));
        contents.Verify(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<DocumentContentInput>(), It.IsAny<ProviderCapabilities>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("901777333-0", "31")]
    [InlineData("900559088-2", "31")]
    [InlineData("79123456", "13")]
    public async Task Issue_IssuerKeptAsItsTypeAsks_Issues(string taxId, string typeCode)
    {
        // Arrange
        StoredIssuer(taxId, typeCode);

        // Act
        await IssueHandler().Handle(Issue(), CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue_DeploymentWithoutTypesWithCheckDigit_IssuesAsBefore()
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitDocumentTypes = [];
        StoredIssuer("901777333");

        // Act
        await IssueHandler(options).Handle(Issue(), CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue_RetryOfADocumentAlreadyReceived_IsAnsweredEvenIfTheIssuerHasNotItsDigit()
    {
        // Arrange
        var command = Issue();
        var existing = TestData.Document(command.Id);

        StoredIssuer("901777333");
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        // Act
        var result = await IssueHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task Retry_CorrectionWithAnIssuerKeptWithoutItsCheckDigit_IsRefusedAndStaysRejected()
    {
        // Arrange
        var document = StoredRejected();

        StoredIssuer("901777333");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => RetryHandler().Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None));

        // Assert
        Assert.Equal((DomainErrors.IssuerTaxIdWithoutItsCheckDigit.GetCode(), ElectronicDocumentStatus.Rejected), (exception.Code, document.Status));
    }

    [Fact]
    public async Task Retry_CorrectionWithAnIssuerKeptWithItsCheckDigit_IsQueuedAgain()
    {
        // Arrange
        var document = StoredRejected();

        StoredIssuer("901777333-0");

        // Act
        await RetryHandler().Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None);

        // Assert
        Assert.Equal(ElectronicDocumentStatus.Draft, document.Status);
    }
}
