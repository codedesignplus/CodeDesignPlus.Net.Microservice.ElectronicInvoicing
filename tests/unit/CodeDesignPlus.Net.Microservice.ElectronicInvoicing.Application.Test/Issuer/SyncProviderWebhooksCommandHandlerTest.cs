using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

/// <summary>
/// An issuer created in the provider before its webhooks were turned on gets them from the round of the webhooks
/// (Kappali pendings/744, §15): Las Acacias and the platform's issuer were adopted in Alanube without them.
/// </summary>
public class SyncProviderWebhooksCommandHandlerTest
{
    private const string Fingerprint = "fingerprint-of-the-current-webhooks";

    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IProviderWebhookRegistrar> registrar = new();
    private readonly Dictionary<Guid, IssuerAggregate> stored = [];

    public SyncProviderWebhooksCommandHandlerTest()
    {
        registrar.Setup(x => x.Code).Returns("Alanube");
        registrar.Setup(x => x.WebhooksFingerprint).Returns(Fingerprint);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => stored.GetValueOrDefault(id)!);
        issuers.Setup(x => x.FindIdsAwaitingWebhooksAsync("Alanube", Fingerprint, It.IsAny<CancellationToken>())).ReturnsAsync(() => [.. stored.Values.Where(x => x.WebhooksFingerprint != Fingerprint).Select(x => x.Id)]);
    }

    [Fact]
    public async Task Handle_IssuerCreatedWithoutWebhooks_RegistersThemAndKeepsTheirFingerprint()
    {
        // Arrange
        var issuer = Provisioned(TestData.Tenant, "company-of-las-acacias");

        // Act
        var registered = await Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None);

        // Assert
        registrar.Verify(x => x.RegisterWebhooksAsync(It.Is<ProviderIssuer>(p => p.ProviderIssuerId == "company-of-las-acacias"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((1, Fingerprint), (registered, issuer.WebhooksFingerprint));
    }

    [Fact]
    public async Task Handle_SecondRound_RegistersNothingAgain()
    {
        // Arrange
        Provisioned(TestData.Tenant, "company-of-las-acacias");
        await Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None);
        registrar.Invocations.Clear();

        // Act
        var registered = await Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None);

        // Assert
        registrar.Verify(x => x.RegisterWebhooksAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, registered);
    }

    [Fact]
    public async Task Handle_WebhooksOff_LooksForNoIssuer()
    {
        // Arrange
        registrar.Setup(x => x.WebhooksFingerprint).Returns((string?)null);
        Provisioned(TestData.Tenant, "company-of-las-acacias");

        // Act
        await Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.FindIdsAwaitingWebhooksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProviderRefusesOneIssuer_RegistersTheOthersAndThrowsAtTheEnd()
    {
        // Arrange
        var refused = Provisioned(TestData.Tenant, "refused-company");
        var other = Provisioned(PlatformScope.Tenant, "platform-company");
        registrar.Setup(x => x.RegisterWebhooksAsync(It.Is<ProviderIssuer>(p => p.ProviderIssuerId == "refused-company"), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "317", "refused"));

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None));

        // Assert
        Assert.Equal((null, Fingerprint), (refused.WebhooksFingerprint, other.WebhooksFingerprint));
        Assert.Contains("1 of 2", error.Message);
    }

    [Fact]
    public async Task Handle_IssuerThatChangedProviderMeanwhile_IsNotTouched()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer("Simulated");
        issuer.MarkProvisioned("simulated-company", TestData.User);
        stored[issuer.Id] = issuer;

        // Act
        await Handler().Handle(new SyncProviderWebhooksCommand(), CancellationToken.None);

        // Assert
        registrar.Verify(x => x.RegisterWebhooksAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(issuer.WebhooksFingerprint);
    }

    private SyncProviderWebhooksCommandHandler Handler() => new(issuers.Object, [registrar.Object, Mock.Of<IElectronicInvoiceProvider>()], Fakes.Options(Fakes.Options()), NullLogger<SyncProviderWebhooksCommandHandler>.Instance);

    private IssuerAggregate Provisioned(Guid tenant, string companyId)
    {
        var issuer = TestData.RegisteredIssuer("Alanube", tenant);

        issuer.MarkProvisioned(companyId, TestData.User);
        issuer.GetAndClearEvents();

        stored[issuer.Id] = issuer;

        return issuer;
    }
}
