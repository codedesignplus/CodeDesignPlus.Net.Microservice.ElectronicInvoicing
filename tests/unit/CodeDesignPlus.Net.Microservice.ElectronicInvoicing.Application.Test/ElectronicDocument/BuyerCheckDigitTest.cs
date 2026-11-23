using System.IO;
using System.Text.Json;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument;

/// <summary>
/// The buyer of a type of identification that carries a check digit, when a document is received and when a rejected one
/// is corrected: it is refused without the digit that belongs to its number, whatever the provider, and the retry of a
/// document already received is answered without checking it again.
/// </summary>
public class BuyerCheckDigitTest
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

    public BuyerCheckDigitTest()
    {
        var range = TestData.Range(issuer);

        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Code).Returns("Simulated");
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        numbering.Setup(x => x.SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(range);
        numbering.Setup(x => x.ReserveAsync(range.Id, It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentNumber("SETP", 990000000));
        quotas.Setup(x => x.ReserveAsync(TestData.Tenant, It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.EmulateOptimisticUpdate<IElectronicDocumentRepository, ElectronicDocumentAggregate>();
    }

    public static TheoryData<string, string?, string> RefusedBuyers => new()
    {
        { "901777333", null, DomainErrors.CheckDigitIsMissing.GetCode() },
        { "9018887773", null, DomainErrors.CheckDigitMayBeAttached.GetCode() },
        { "901777333", "5", DomainErrors.CheckDigitDoesNotMatch.GetCode() },
        { "901777333-0", null, DomainErrors.CheckDigitFormatIsInvalid.GetCode() }
    };

    private static DocumentContent Content(string typeCode, string number, string? checkDigit) => TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { IdentificationTypeCode = typeCode, IdentificationNumber = number, CheckDigit = checkDigit } };

    private void Resolves(DocumentContent content) => contents.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<DocumentContentInput>(), It.IsAny<ProviderCapabilities>(), It.IsAny<CancellationToken>())).ReturnsAsync(content);

    private IssueElectronicDocumentCommandHandler IssueHandler(ElectronicInvoicingOptions? options = null) => new(repository.Object, issuers.Object, factory.Object, contents.Object, numbering.Object, quotas.Object, Mock.Of<ITestProviderGrantRepository>(), user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(options ?? Fakes.Options()), NullLogger<IssueElectronicDocumentCommandHandler>.Instance);

    private RetryElectronicDocumentCommandHandler RetryHandler(ElectronicInvoicingOptions? options = null) => new(repository.Object, issuers.Object, factory.Object, contents.Object, user.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(options ?? Fakes.Options()));

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

    private static ElectronicInvoicingOptions WithoutBuyerTypes()
    {
        var options = Fakes.Options();

        options.BuyerCheckDigitDocumentTypes = [];

        return options;
    }

    [Theory]
    [MemberData(nameof(RefusedBuyers))]
    public async Task Issue_BuyerWithoutItsRightCheckDigit_IsRefusedBeforeTheQuotaAndNothingIsSaved(string number, string? checkDigit, string code)
    {
        // Arrange: the issuer issues with the simulated provider, which does not skip the rule.
        Resolves(Content("31", number, checkDigit));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => IssueHandler().Handle(Issue(), CancellationToken.None));

        // Assert
        Assert.Equal((code, Layer.Domain), (exception.Code, exception.Layer));
        quotas.Verify(x => x.ReserveAsync(It.IsAny<Guid>(), It.IsAny<ElectronicDocumentType>(), It.IsAny<Instant>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Issue_BuyerWithItsCheckDigit_IsReceived()
    {
        // Arrange
        Resolves(Content("31", "901777333", "0"));

        // Act
        await IssueHandler().Handle(Issue(), CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.Is<ElectronicDocumentAggregate>(document => document.Content.Buyer.CheckDigit == "0"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue_FinalConsumer_IsReceivedWithoutDigit()
    {
        // Arrange
        Resolves(Content("13", "222222222222", null));

        // Act
        await IssueHandler().Handle(Issue(), CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.Is<ElectronicDocumentAggregate>(document => document.Content.Buyer.IdentificationNumber == "222222222222"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue_DeploymentThatChecksNoBuyer_ReceivesItAsBefore()
    {
        // Arrange
        Resolves(Content("31", "901777333", null));

        // Act
        await IssueHandler(WithoutBuyerTypes()).Handle(Issue(), CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue_RetryOfADocumentReceivedWithoutTheDigit_ReturnsItWithoutCheckingItAgain()
    {
        // Arrange: the document was received before the rule, with the same content the retry brings.
        var content = Content("31", "901777333", null);
        var command = Issue();
        var existing = TestData.Document(command.Id, content: content);

        Resolves(content);
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        // Act
        var result = await IssueHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(existing.Id, result.Id);
    }

    [Theory]
    [MemberData(nameof(RefusedBuyers))]
    public async Task Retry_CorrectedContentWhoseBuyerHasNotItsRightCheckDigit_IsRefusedAndStaysRejected(string number, string? checkDigit, string code)
    {
        // Arrange
        var document = StoredRejected();

        Resolves(Content("31", number, checkDigit));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => RetryHandler().Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None));

        // Assert
        Assert.Equal((code, ElectronicDocumentStatus.Rejected), (exception.Code, document.Status));
    }

    [Fact]
    public async Task Retry_CorrectedContentWithTheDigitOfItsBuyer_IsQueuedAgain()
    {
        // Arrange
        var document = StoredRejected();

        Resolves(Content("31", "901777333", "0"));

        // Act
        await RetryHandler().Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None);

        // Assert
        Assert.Equal((ElectronicDocumentStatus.Draft, "0"), (document.Status, document.Content.Buyer.CheckDigit));
    }

    [Fact]
    public async Task Retry_DeploymentThatChecksNoBuyer_CorrectsItAsBefore()
    {
        // Arrange
        var document = StoredRejected();

        Resolves(Content("31", "901777333", null));

        // Act
        await RetryHandler(WithoutBuyerTypes()).Handle(new RetryElectronicDocumentCommand(document.Id, null, Input()), CancellationToken.None);

        // Assert
        Assert.Equal(ElectronicDocumentStatus.Draft, document.Status);
    }

    [Fact]
    public void Validate_BuyerTypesWithoutWeights_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitDocumentTypes = [];
        options.CheckDigitWeights = [];

        // Act
        var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.BuyerCheckDigitDocumentTypes)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyBuyerTypeCode_SaysSo(string code)
    {
        // Arrange
        var options = Fakes.Options();

        options.BuyerCheckDigitDocumentTypes = ["31", code];

        // Act
        var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.BuyerCheckDigitDocumentTypes)));
    }

    [Fact]
    public void ToBuyerCheckDigitRules_ConfiguredTypes_AreTheOnesOfTheBuyersWithTheSharedWeights()
    {
        // Arrange
        var options = Fakes.Options();

        options.BuyerCheckDigitDocumentTypes = ["41"];

        // Act
        var rules = options.ToBuyerCheckDigitRules();

        // Assert
        Assert.Equal((true, false, 0), (rules.Applies("41"), rules.Applies("31"), rules.Compute("901777333")));
    }

    [Theory]
    [InlineData("Rest")]
    [InlineData("gRpc")]
    [InlineData("AsyncWorker")]
    public void ShippedSettings_EveryEntrypoint_CheckTheBuyersOfTheSameTypesAsTheIssuers(string entrypoint)
    {
        // Arrange
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && root.GetFiles("*.sln").Length == 0)
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        var path = Path.Combine(root.FullName, "src", "entrypoints", $"CodeDesignPlus.Net.Microservice.ElectronicInvoicing.{entrypoint}", "appsettings.json");

        using var settings = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var section = settings.RootElement.GetProperty(ElectronicInvoicingOptions.Section);

        // Act
        var options = new ElectronicInvoicingOptions
        {
            CheckDigitDocumentTypes = [.. section.GetProperty(nameof(ElectronicInvoicingOptions.CheckDigitDocumentTypes)).EnumerateArray().Select(x => x.GetString()!)],
            BuyerCheckDigitDocumentTypes = [.. section.GetProperty(nameof(ElectronicInvoicingOptions.BuyerCheckDigitDocumentTypes)).EnumerateArray().Select(x => x.GetString()!)],
            CheckDigitWeights = [.. section.GetProperty(nameof(ElectronicInvoicingOptions.CheckDigitWeights)).EnumerateArray().Select(x => x.GetInt32())]
        };
        var rules = options.ToBuyerCheckDigitRules();

        // Assert: the check of the buyers is on, for the same types the issuers are held to, and it tells a buyer with its
        // digit from one without it.
        Assert.NotEmpty(options.BuyerCheckDigitDocumentTypes);
        Assert.Equal(options.CheckDigitDocumentTypes, options.BuyerCheckDigitDocumentTypes);
        Assert.All(options.BuyerCheckDigitDocumentTypes, type => Assert.Null(Record.Exception(() => rules.EnsureSeparate(type, "901777333", "0"))));
        Assert.All(options.BuyerCheckDigitDocumentTypes, type => Assert.Null(Record.Exception(() => rules.EnsureSeparate(type, "79123456", "0"))));
        Assert.All(options.BuyerCheckDigitDocumentTypes, type => Assert.Equal(DomainErrors.CheckDigitIsMissing.GetCode(), Assert.Throws<CodeDesignPlusException>(() => rules.EnsureSeparate(type, "901777333", null)).Code));
    }
}
