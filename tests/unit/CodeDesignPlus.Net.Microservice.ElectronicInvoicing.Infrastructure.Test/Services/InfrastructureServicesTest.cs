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
    private static SimulatedProvider Provider(params SimulatedRule[] rules) => new(Microsoft.Extensions.Options.Options.Create(new SimulatedOptions { Rules = [.. rules] }));

    private static ProviderIssuer Issuer() => ProviderIssuer.From(TestData.EnabledIssuer(), new Dictionary<string, string>());

    private static ProviderDocument Document(string identification = "900000001") => new(Guid.Parse("bb000000-0000-4000-8000-000000000001"), ElectronicDocumentType.Invoice, new DocumentNumber("SETP", 1), null, TestData.InvoiceContent(identification), "key");

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
    private readonly ElectronicInvoiceProviderFactory factory = new([new SimulatedProvider(Microsoft.Extensions.Options.Options.Create(new SimulatedOptions())), new FakeAliaddoProvider().Provider]);

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
