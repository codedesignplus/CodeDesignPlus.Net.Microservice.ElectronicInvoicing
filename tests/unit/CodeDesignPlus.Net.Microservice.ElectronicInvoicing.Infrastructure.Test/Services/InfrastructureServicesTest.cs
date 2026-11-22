using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Vault;
using CodeDesignPlus.Net.Vault.Abstractions;
using CodeDesignPlus.Net.Vault.Abstractions.Options;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;
using InfraErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

public class SimulatedProviderTest
{
    private static SimulatedProvider Provider(params SimulatedRule[] rules) => Simulated.SimulatedProviders.Create(new SimulatedOptions { Rules = [.. rules] });

    private static ProviderIssuer Issuer() => ProviderIssuer.From(TestData.EnabledIssuer(), new Dictionary<string, string>());

    private static ProviderDocument Document(string identification = "900000001") => new(Guid.Parse("bb000000-0000-4000-8000-000000000001"), ElectronicDocumentType.Invoice, new DocumentNumber("SETP", 1), null, TestData.InvoiceContent(identification), "key", new LocalDate(2026, 11, 10));

    [Fact]
    public async Task SubmitAsync_NoRule_AcceptsWithADeterministicCufe()
    {
        // Act
        var first = await Provider().SubmitAsync(Issuer(), Document(), CancellationToken.None);
        var second = await Provider().SubmitAsync(Issuer(), Document(), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, first.State.Cufe, first.ProviderDocumentId), (first.State.Status, second.State.Cufe, second.ProviderDocumentId));
    }

    [Fact]
    public async Task SubmitAsync_RuleToReject_RejectsWithItsReason()
    {
        // Act
        var submission = await Provider(new SimulatedRule { BuyerIdentification = "222222222", Outcome = SimulatedOutcome.Rejected, Reason = "Rechazo simulado." }).SubmitAsync(Issuer(), Document("222222222"), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, "Rechazo simulado."), (submission.State.Status, submission.State.Reasons.Single()));
    }

    [Fact]
    public async Task GetStateAsync_TaxAuthorityNotAnswering_IsAcceptedAfterTheConfiguredChecks()
    {
        // Arrange
        var provider = Provider(new SimulatedRule { BuyerIdentification = "333333333", Outcome = SimulatedOutcome.DianUnavailable, ChecksUntilResolved = 2 });
        var submission = await provider.SubmitAsync(Issuer(), Document("333333333"), CancellationToken.None);
        var lookup = new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key");

        // Act
        var first = await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);
        var second = await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.DianUnavailable, ProviderDocumentStatus.DianUnavailable, ProviderDocumentStatus.Accepted), (submission.State.Status, first!.Status, second!.Status));
    }

    [Fact]
    public async Task SubmitAsync_RuleOfNoAnswer_ThrowsProviderUnavailable()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Provider(new SimulatedRule { BuyerIdentification = "444444444", Outcome = SimulatedOutcome.ProviderUnavailable }).SubmitAsync(Issuer(), Document("444444444"), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_RuleOfNoAnswer_KeepsNothing()
    {
        // Arrange
        var documents = new Simulated.InMemorySimulatedDocuments();
        var provider = Simulated.SimulatedProviders.Create(new SimulatedOptions { Rules = [new SimulatedRule { BuyerIdentification = "444444444", Outcome = SimulatedOutcome.ProviderUnavailable }] }, documents);

        // Act
        await Assert.ThrowsAsync<CodeDesignPlusException>(() => provider.SubmitAsync(Issuer(), Document("444444444"), CancellationToken.None));

        // Assert
        Assert.Equal(0, documents.Count);
    }

    [Fact]
    public async Task GetStateAsync_ChecksAskedToTwoProvidersOverTheSameDocuments_AddUp()
    {
        // Arrange: two replicas of the service, each with its provider, over what both keep.
        var rule = new SimulatedOptions { Rules = [new SimulatedRule { BuyerIdentification = "333333333", Outcome = SimulatedOutcome.DianUnavailable, ChecksUntilResolved = 2 }] };
        var documents = new Simulated.InMemorySimulatedDocuments();
        var first = Simulated.SimulatedProviders.Create(rule, documents);
        var second = Simulated.SimulatedProviders.Create(rule, documents);

        var submission = await first.SubmitAsync(Issuer(), Document("333333333"), CancellationToken.None);
        var lookup = new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key");

        // Act
        var one = await second.GetStateAsync(Issuer(), lookup, CancellationToken.None);
        var two = await first.GetStateAsync(Issuer(), lookup, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.DianUnavailable, ProviderDocumentStatus.Accepted), (one!.Status, two!.Status));
    }

    [Fact]
    public async Task GetStateAsync_ProcessingForSomeChecks_IsAcceptedAfterThem()
    {
        // Arrange
        var provider = Provider(new SimulatedRule { BuyerIdentification = "555555555", Outcome = SimulatedOutcome.Processing, ChecksUntilResolved = 2 });
        var submission = await provider.SubmitAsync(Issuer(), Document("555555555"), CancellationToken.None);
        var lookup = new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key");

        // Act
        var first = await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);
        var second = await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Processing, ProviderDocumentStatus.Processing, ProviderDocumentStatus.Accepted), (submission.State.Status, first!.Status, second!.Status));
    }

    [Fact]
    public async Task GetStateAsync_RejectedDocument_StaysRejectedHoweverManyChecks()
    {
        // Arrange
        var provider = Provider(new SimulatedRule { BuyerIdentification = "222222222", Outcome = SimulatedOutcome.Rejected, Reason = "Rechazo simulado.", ChecksUntilResolved = 1 });
        var submission = await provider.SubmitAsync(Issuer(), Document("222222222"), CancellationToken.None);
        var lookup = new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key");

        // Act
        await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);
        var state = await provider.GetStateAsync(Issuer(), lookup, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, "Rechazo simulado."), (state!.Status, state.Reasons.Single()));
    }

    [Fact]
    public async Task GetStateAsync_RuleChangedAfterTheDocumentArrived_FollowsTheRuleItArrivedWith()
    {
        // Arrange: the document arrived to wait; a replica started later with other rules must not change what happened to it.
        var documents = new Simulated.InMemorySimulatedDocuments();
        var received = Simulated.SimulatedProviders.Create(new SimulatedOptions { Rules = [new SimulatedRule { BuyerIdentification = "333333333", Outcome = SimulatedOutcome.DianUnavailable, ChecksUntilResolved = 5 }] }, documents);
        var restarted = Simulated.SimulatedProviders.Create(new SimulatedOptions(), documents);

        var submission = await received.SubmitAsync(Issuer(), Document("333333333"), CancellationToken.None);

        // Act
        var state = await restarted.GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.DianUnavailable, state!.Status);
    }

    [Fact]
    public async Task GetStateAsync_DocumentNeverSent_IsNotFound()
    {
        // Act
        var state = await Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, Guid.NewGuid().ToString(), new DocumentNumber("SETP", 1), "key"), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetFilesAsync_DocumentNeverSent_HandsOverNoFiles()
    {
        // Act
        var files = await Provider().GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, Guid.NewGuid().ToString(), null, "key"), CancellationToken.None);

        // Assert
        Assert.Same(ProviderFiles.None, files);
    }

    private static ProviderDocument PosTicket(DocumentContent content) => new(Guid.Parse("bb000000-0000-4000-8000-000000000002"), ElectronicDocumentType.PosTicket, new DocumentNumber("POS", 1), null, content, "key", new LocalDate(2026, 11, 10));

    [Fact]
    public async Task SubmitAsync_PosTicketWithItsRegister_IsAccepted()
    {
        // Act
        var submission = await Provider().SubmitAsync(Issuer(), PosTicket(TestData.PosTicketContent()), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    [Fact]
    public async Task SubmitAsync_PosTicketWithoutItsRegister_IsRejectedAsTheTaxAuthorityWould()
    {
        // Act: a ticket saved without the register reaches the provider.
        var submission = await Provider().SubmitAsync(Issuer(), PosTicket(TestData.InvoiceContent()), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, SimulatedProvider.MissingPointOfSaleReason, RejectionOrigin.Dian), (submission.State.Status, submission.State.Reasons.Single(), submission.State.RejectionOrigin));
    }

    [Fact]
    public async Task SubmitAsync_PosTicketWithARegisterThatMissesAText_IsRejected()
    {
        // Arrange
        var content = TestData.PosTicketContent() with { PointOfSale = TestData.PointOfSale() with { RegisterCode = string.Empty } };

        // Act
        var submission = await Provider().SubmitAsync(Issuer(), PosTicket(content), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Rejected, submission.State.Status);
    }

    [Fact]
    public async Task SubmitAsync_PosTicketOfABuyerWithARule_FollowsTheRule()
    {
        // Act: the register is there, so the rule of the buyer decides.
        var submission = await Provider(new SimulatedRule { BuyerIdentification = "222222222", Outcome = SimulatedOutcome.Rejected, Reason = "Rechazo simulado." }).SubmitAsync(Issuer(), PosTicket(TestData.PosTicketContent("222222222")), CancellationToken.None);

        // Assert
        Assert.Equal("Rechazo simulado.", submission.State.Reasons.Single());
    }

    [Fact]
    public async Task GetFilesAsync_PosTicket_KeepsTheRegisterItReceivedInItsXml()
    {
        // Arrange
        var provider = Provider();
        var submission = await provider.SubmitAsync(Issuer(), PosTicket(TestData.PosTicketContent()), CancellationToken.None);

        // Act
        var files = await provider.GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.PosTicket, submission.ProviderDocumentId, null, "key"), CancellationToken.None);

        // Assert
        var register = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(files.Xml!.Content)).Root!.Element("PointOfSale")!;

        Assert.Equal(("REG-001", "Standard", "Punto de Prueba", "Calle 9 # 8-7", "Cajera de Prueba", "SALE-0001", "2000000"), (register.Attribute("registerCode")!.Value, register.Attribute("registerType")!.Value, register.Attribute("locationName")!.Value, register.Attribute("locationAddress")!.Value, register.Attribute("cashierName")!.Value, register.Attribute("saleCode")!.Value, register.Attribute("subtotal")!.Value));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_HasNoRegisterInItsXml()
    {
        // Arrange
        var provider = Provider();
        var submission = await provider.SubmitAsync(Issuer(), Document(), CancellationToken.None);

        // Act
        var files = await provider.GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key"), CancellationToken.None);

        // Assert
        Assert.Null(System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(files.Xml!.Content)).Root!.Element("PointOfSale"));
    }

    [Fact]
    public async Task GetStateAsync_ByNumber_FindsASendWhoseAnswerWasLost()
    {
        // Arrange
        var provider = Provider();

        await provider.SubmitAsync(Issuer(), Document(), CancellationToken.None);

        // Act
        var state = await provider.GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, null, new DocumentNumber("SETP", 1), "key"), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, state!.Status);
    }
}

public class VaultIssuerSecretCipherTest
{
    private const string KeyName = "electronic-issuers";

    private readonly Mock<IVaultTransit> transit = new();

    private VaultIssuerSecretCipher Cipher(bool withContext = true)
    {
        var vault = new VaultOptions();

        if (withContext)
        {
            vault.Transit.SecretContexts[VaultIssuerSecretCipher.SecretContextName] = "electronic-issuer";
        }

        return new VaultIssuerSecretCipher(transit.Object, Microsoft.Extensions.Options.Options.Create(vault), Microsoft.Extensions.Options.Options.Create(new ElectronicInvoicingOptions { SecretsKeyName = KeyName }), NullLogger<VaultIssuerSecretCipher>.Instance);
    }

    [Fact]
    public async Task SealAsync_Secrets_UseTheFixedKeyAndTheTenantContextInTheOrderOfTheirNames()
    {
        // Arrange
        transit.Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(["c-a", "c-b"]);

        // Act
        var sealedSecrets = await Cipher().SealAsync(TestData.Tenant, new Dictionary<string, string> { ["b"] = "plain-b", ["a"] = "plain-a" }, CancellationToken.None);

        // Assert
        transit.Verify(x => x.EncryptAsync(KeyName, It.Is<List<string>>(plain => plain.SequenceEqual(new[] { "plain-a", "plain-b" })), $"electronic-issuer:{TestData.Tenant}"), Times.Once);

        Assert.Equal(("c-a", "c-b"), (sealedSecrets["a"], sealedSecrets["b"]));
    }

    [Fact]
    public async Task OpenAsync_VaultDoesNotAnswer_ThrowsVaultUnavailable()
    {
        // Arrange
        transit.Setup(x => x.DecryptAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Cipher().OpenAsync(TestData.Tenant, new Dictionary<string, string> { ["a"] = "c-a" }, CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.VaultUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SealAsync_ContextNotConfigured_ThrowsSecretContextNotConfigured()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Cipher(false).SealAsync(TestData.Tenant, new Dictionary<string, string> { ["a"] = "plain" }, CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.SecretContextNotConfigured.GetCode(), exception.Code);
    }
}

public class ElectronicInvoiceProviderFactoryTest
{
    private readonly ElectronicInvoiceProviderFactory factory = new([Simulated.SimulatedProviders.Create(), new FakeAliaddoProvider().Provider]);

    [Fact]
    public void Get_UnknownProvider_ThrowsProviderNotSupported()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => factory.Get("Unknown"));

        // Assert
        Assert.Equal(AppErrors.ProviderNotSupported.GetCode(), exception.Code);
    }

    [Fact]
    public void FindEvents_ProviderThatCallsBack_IsFoundWhateverTheCase()
    {
        // Act & Assert
        Assert.NotNull(factory.FindEvents("aliaddo"));
    }

    [Fact]
    public void FindEvents_ProviderThatDoesNotCallBack_IsNull()
    {
        // Act & Assert
        Assert.Null(factory.FindEvents(SimulatedProvider.ProviderCode));
    }

    private sealed class FakeAliaddoProvider
    {
        public AliaddoProvider Provider { get; } = new Aliaddo.FakeAliaddo().Provider();
    }
}

public class TransientFailuresTest
{
    [Fact]
    public void IsTransient_CodesOfTheInfrastructure_AreTheProviderAndTheVaultNotAnswering()
    {
        // Act & Assert: the application knows them by their code, so they cannot drift apart.
        Assert.Equal((true, true, false), (TransientFailures.IsTransient(new CodeDesignPlusException(Layer.Infrastructure, InfraErrors.ProviderUnavailable.GetCode(), "x")), TransientFailures.IsTransient(new CodeDesignPlusException(Layer.Infrastructure, InfraErrors.VaultUnavailable.GetCode(), "x")), TransientFailures.IsTransient(new CodeDesignPlusException(Layer.Infrastructure, InfraErrors.ProviderCredentialsRejected.GetCode(), "x"))));
    }

    [Fact]
    public void IsCredentialsRejection_CodeOfTheInfrastructure_IsTheRejectedCredentials()
    {
        // Act & Assert
        Assert.True(TransientFailures.IsCredentialsRejection(new CodeDesignPlusException(Layer.Infrastructure, InfraErrors.ProviderCredentialsRejected.GetCode(), "x")));
    }
}

/// <summary>
/// The settings shipped with each entrypoint pass the validation that runs on start, and carry no secret.
/// </summary>
public class ShippedSettingsTest
{
    public static TheoryData<string> Entrypoints() => new()
    {
        "CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest",
        "CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc",
        "CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker"
    };

    [Theory]
    [MemberData(nameof(Entrypoints))]
    public void ElectronicInvoicing_ShippedSettings_AreValidAndRouteColombiaToAliaddo(string entrypoint)
    {
        // Arrange
        var options = Settings(entrypoint).GetSection(ElectronicInvoicingOptions.Section).Get<ElectronicInvoicingOptions>()!;

        // Act
        var errors = Validate(options);

        // Assert
        Assert.Equal((0, "Aliaddo", 500L, 40_000L), (errors.Count, options.ProvidersFor("CO")[0], options.PlatformIssuer.ReservedDocuments, options.ProviderPlan.Documents));
    }

    [Theory]
    [MemberData(nameof(Entrypoints))]
    public void ElectronicInvoicing_DevelopmentSettings_RouteToTheSimulatedProvider(string entrypoint)
    {
        // Arrange
        var options = Settings(entrypoint, "Development").GetSection(ElectronicInvoicingOptions.Section).Get<ElectronicInvoicingOptions>()!;

        // Act & Assert
        Assert.Equal(SimulatedProvider.ProviderCode, options.ProvidersFor("CO")[0]);
    }

    [Theory]
    [MemberData(nameof(Entrypoints))]
    public void ElectronicInvoicing_ShippedSettings_KeepTheSimulatedProviderForTheTenantsWithPermission(string entrypoint)
    {
        // Arrange
        var options = Settings(entrypoint).GetSection(ElectronicInvoicingOptions.Section).Get<ElectronicInvoicingOptions>()!;

        // Act & Assert: it is a test provider, the routing does not offer it to everyone, and it is never the default.
        Assert.Equal((true, true, false), (options.TestProviders.SequenceEqual([SimulatedProvider.ProviderCode]), options.NeedsTestProviderGrant("CO", SimulatedProvider.ProviderCode), options.ProvidersFor("CO").Contains(SimulatedProvider.ProviderCode)));
    }

    [Theory]
    [MemberData(nameof(Entrypoints))]
    public void ElectronicInvoicing_DevelopmentSettings_NeedNoPermissionForTheSimulatedProvider(string entrypoint)
    {
        // Arrange
        var options = Settings(entrypoint, "Development").GetSection(ElectronicInvoicingOptions.Section).Get<ElectronicInvoicingOptions>()!;

        // Act & Assert
        Assert.Equal((true, false), (options.IsTestProvider(SimulatedProvider.ProviderCode), options.NeedsTestProviderGrant("CO", SimulatedProvider.ProviderCode)));
    }

    [Theory]
    [MemberData(nameof(Entrypoints))]
    public void Aliaddo_ShippedSettings_AreValidAndCarryNoToken(string entrypoint)
    {
        // Arrange: the token of the platform comes from the Vault, never from a file in the repository.
        var options = Settings(entrypoint).GetSection(AliaddoOptions.Section).Get<AliaddoOptions>()!;

        // Act
        var errors = Validate(options);

        // Assert
        Assert.Equal((0, true, false, true), (errors.Count, string.IsNullOrEmpty(options.Master.Token), options.Webhooks.Enabled, string.IsNullOrEmpty(options.Webhooks.Secret)));
    }

    private static List<ValidationResult> Validate(object options)
    {
        var errors = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), errors, validateAllProperties: true);

        if (options is IValidatableObject validatable)
        {
            errors.AddRange(validatable.Validate(new ValidationContext(options)));
        }

        return errors;
    }

    private static IConfiguration Settings(string entrypoint, string? environment = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "entrypoints")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory!.FullName, "src", "entrypoints", entrypoint);
        var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json"));

        if (environment is not null)
        {
            builder.AddJsonFile(Path.Combine(folder, $"appsettings.{environment}.json"), optional: true);
        }

        return builder.Build();
    }
}
