using System.IO;
using System.Text.Json;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.UpdateIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

/// <summary>
/// Registering or editing an issuer whose type of identification carries a check digit: the commands take the rules from
/// the options of the deployment, for the tenant's issuer and for the platform's, whatever the provider.
/// </summary>
public class IssuerTaxIdCheckDigitTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();

    public IssuerTaxIdCheckDigitTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Code).Returns("Simulated");
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Find("Simulated")).Returns(provider.Object);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
    }

    public static TheoryData<string, string> RefusedTaxIds => new()
    {
        { "901777333", DomainErrors.CheckDigitIsMissing.GetCode() },
        { "9018887773", DomainErrors.CheckDigitMayBeAttached.GetCode() },
        { "901888777-5", DomainErrors.CheckDigitDoesNotMatch.GetCode() },
        { "901.888.777-3", DomainErrors.CheckDigitFormatIsInvalid.GetCode() }
    };

    private static RegisterIssuerCommand Register(string taxId, string typeCode = "31") => new("CO", typeCode, taxId, "Emisor de Prueba SAS", "1", "48", ["O-13"], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota", "Simulated");

    private static UpdateIssuerCommand Update(string taxId, string typeCode = "31") => new("CO", typeCode, taxId, "Emisor de Prueba SAS", "1", "48", ["O-13"], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota");

    private RegisterIssuerCommandHandler RegisterHandler(ElectronicInvoicingOptions? options = null) => new(issuers.Object, factory.Object, Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Options(options ?? Fakes.Options()));

    private UpdateIssuerCommandHandler UpdateHandler(ElectronicInvoicingOptions? options = null) => new(issuers.Object, pubsub.Object, new RecordingAvailability(), user.Object, Fakes.Options(options ?? Fakes.Options()));

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public async Task RegisterIssuer_TaxIdWithoutItsRightCheckDigit_IsRefusedAndNothingIsSaved(string taxId, string code)
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => RegisterHandler().Handle(Register(taxId), CancellationToken.None));

        // Assert
        Assert.Equal(code, exception.Code);
        issuers.Verify(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public async Task RegisterIssuer_PlatformIssuer_IsRefusedTheSame(string taxId, string code)
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => RegisterHandler().Handle(Register(taxId) with { PlatformIssuer = true }, CancellationToken.None));

        // Assert
        Assert.Equal(code, exception.Code);
        issuers.Verify(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterIssuer_TaxIdWithItsCheckDigit_IsSavedAsWritten()
    {
        // Act
        await RegisterHandler().Handle(Register("901777333-0"), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.TaxId == "901777333-0"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterIssuer_TypeWithoutCheckDigit_NeedsNone()
    {
        // Act
        await RegisterHandler().Handle(Register("79123456", "13"), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.TaxId == "79123456"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterIssuer_DeploymentWithoutTypesWithCheckDigit_NeedsNone()
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitDocumentTypes = [];
        options.CheckDigitWeights = [];

        // Act
        await RegisterHandler(options).Handle(Register("901888777"), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.TaxId == "901888777"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public async Task UpdateIssuer_TaxIdWithoutItsRightCheckDigit_IsRefusedAndKeepsTheOneItHad(string taxId, string code)
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => UpdateHandler().Handle(Update(taxId), CancellationToken.None));

        // Assert
        Assert.Equal(code, exception.Code);
        Assert.Equal("900000002-1", issuer.TaxId);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public async Task UpdateIssuer_PlatformIssuer_IsRefusedTheSame(string taxId, string code)
    {
        // Arrange
        var issuer = TestData.EnabledIssuer(tenant: PlatformScope.Tenant);

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(PlatformScope.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => UpdateHandler().Handle(Update(taxId) with { PlatformIssuer = true }, CancellationToken.None));

        // Assert
        Assert.Equal(code, exception.Code);
        Assert.Equal("900000002-1", issuer.TaxId);
    }

    [Fact]
    public async Task UpdateIssuer_TaxIdWithItsCheckDigit_ReplacesIt()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        // Act
        await UpdateHandler().Handle(Update("901777333-0"), CancellationToken.None);

        // Assert
        Assert.Equal("901777333-0", issuer.TaxId);
    }

    [Theory]
    [InlineData("Rest")]
    [InlineData("gRpc")]
    [InlineData("AsyncWorker")]
    public void ShippedSettings_EveryEntrypoint_CarryTheTypesAndTheWeightsOfTheCheckDigit(string entrypoint)
    {
        // Arrange
        var options = ShippedOptions(entrypoint);

        // Act
        var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).Where(x => x.MemberNames.Any(member => member.StartsWith("CheckDigit", StringComparison.Ordinal))).ToList();
        var rules = options.ToCheckDigitRules();

        // Assert: an issuer already registered with its digit stays valid, and one without it would be refused.
        Assert.Empty(problems);
        Assert.NotEmpty(options.CheckDigitDocumentTypes);
        Assert.All(options.CheckDigitDocumentTypes, type => Assert.Null(Record.Exception(() => rules.Ensure(type, "901777333-0"))));
        Assert.All(options.CheckDigitDocumentTypes, type => Assert.Null(Record.Exception(() => rules.Ensure(type, "79123456-0"))));
        Assert.All(options.CheckDigitDocumentTypes, type => Assert.Null(Record.Exception(() => rules.Ensure(type, "900000002-1"))));
        Assert.All(options.CheckDigitDocumentTypes, type => Assert.Equal(DomainErrors.CheckDigitIsMissing.GetCode(), Assert.Throws<CodeDesignPlusException>(() => rules.Ensure(type, "901777333")).Code));
    }

    /// <summary>
    /// The check digit settings of an entrypoint as they are shipped in its <c>appsettings.json</c>.
    /// </summary>
    private static ElectronicInvoicingOptions ShippedOptions(string entrypoint)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && root.GetFiles("*.sln").Length == 0)
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        var path = Path.Combine(root.FullName, "src", "entrypoints", $"CodeDesignPlus.Net.Microservice.ElectronicInvoicing.{entrypoint}", "appsettings.json");

        using var settings = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var section = settings.RootElement.GetProperty(ElectronicInvoicingOptions.Section);

        return new ElectronicInvoicingOptions
        {
            CheckDigitDocumentTypes = [.. section.GetProperty(nameof(ElectronicInvoicingOptions.CheckDigitDocumentTypes)).EnumerateArray().Select(x => x.GetString()!)],
            CheckDigitWeights = [.. section.GetProperty(nameof(ElectronicInvoicingOptions.CheckDigitWeights)).EnumerateArray().Select(x => x.GetInt32())]
        };
    }
}

/// <summary>
/// The check digit settings are validated on start.
/// </summary>
public class CheckDigitOptionsTest
{
    private static List<System.ComponentModel.DataAnnotations.ValidationResult> ProblemsOf(ElectronicInvoicingOptions options) => [.. options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))];

    [Fact]
    public void Validate_WithoutTypesWithCheckDigit_HasNoProblems()
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitDocumentTypes = [];
        options.BuyerCheckDigitDocumentTypes = [];
        options.CheckDigitWeights = [];

        // Act
        var problems = ProblemsOf(options);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void Validate_TypesWithoutWeights_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitWeights = [];

        // Act
        var problems = ProblemsOf(options);

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.CheckDigitWeights)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Validate_WeightThatIsNotPositive_SaysSo(int weight)
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitWeights = [3, weight, 13];

        // Act
        var problems = ProblemsOf(options);

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.CheckDigitWeights)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyTypeCode_SaysSo(string code)
    {
        // Arrange
        var options = Fakes.Options();

        options.CheckDigitDocumentTypes = ["31", code];

        // Act
        var problems = ProblemsOf(options);

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.CheckDigitDocumentTypes)));
    }

    [Fact]
    public void ToCheckDigitRules_ConfiguredTypesAndWeights_AreTheOnesTheDomainReads()
    {
        // Act
        var rules = Fakes.Options().ToCheckDigitRules();

        // Assert
        Assert.Equal((true, 3), (rules.Applies("31"), rules.Compute("901888777")));
    }
}
