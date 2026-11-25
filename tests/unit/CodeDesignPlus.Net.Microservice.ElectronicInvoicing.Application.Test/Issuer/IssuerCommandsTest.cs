using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

public class IssuerCommandsTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();

    public IssuerCommandsTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Code).Returns("Simulated");
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Find("Simulated")).Returns(provider.Object);
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
    }

    private static RegisterIssuerCommand Register(string? provider = null) => new("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", "48", ["O-13"], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota", provider);

    [Fact]
    public async Task RegisterIssuer_WithoutProvider_TakesTheDefaultOfTheCountry()
    {
        // Arrange
        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, Mock.Of<ITestProviderGrantRepository>(), Mock.Of<IManagedFiscalProfileRepository>(), Fakes.Clock(), user.Object, Fakes.Options(Fakes.Options()));

        // Act
        await handler.Handle(Register(), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.Provider == "Simulated" && issuer.Tenant == TestData.Tenant && issuer.Status == IssuerStatus.Draft), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterIssuer_ProviderTheCountryDoesNotAllow_ThrowsProviderNotSupported()
    {
        // Arrange
        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, Mock.Of<ITestProviderGrantRepository>(), Mock.Of<IManagedFiscalProfileRepository>(), Fakes.Clock(), user.Object, Fakes.Options(Fakes.Options()));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Register("Unknown"), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.ProviderNotSupported.GetCode(), exception.Code);
    }

    [Fact]
    public async Task RegisterIssuer_AlreadyRegistered_EndsWellWithoutRegisteringAgain()
    {
        // Arrange
        issuers.Setup(x => x.ExistsAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, Mock.Of<ITestProviderGrantRepository>(), Mock.Of<IManagedFiscalProfileRepository>(), Fakes.Clock(), user.Object, Fakes.Options(Fakes.Options()));

        // Act
        await handler.Handle(Register(), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionIssuer_AlreadyInTheProvider_EndsWellWithoutCreatingASecondOne()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();
        var provisioning = new Mock<IIssuerProvisioningProvider>();

        issuer.MarkProvisioned("child-1", TestData.User);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        factory.Setup(x => x.FindProvisioning("Simulated")).Returns(provisioning.Object);

        var handler = new ProvisionIssuerCommandHandler(issuers.Object, factory.Object, pubsub.Object, new RecordingAvailability(), user.Object);

        // Act
        await handler.Handle(new ProvisionIssuerCommand("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.ProvisionAsync(It.IsAny<ProviderIssuer>(), It.IsAny<IssuerContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetIssuer_TenantWithoutIssuer_ReturnsNull()
    {
        // Arrange
        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), Mock.Of<IManagedFiscalProfileRepository>());

        // Act
        var issuer = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Null(issuer);
    }

    [Fact]
    public async Task GetIssuer_EnabledIssuerWithQuota_SaysWhatIsLeft()
    {
        // Arrange
        var quotas = new Mock<IQuotaGate>();
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.Tenant, TestData.SystemUser);

        quota.ApplyAllowance(100, TestData.Now + Duration.FromDays(300), TestData.Now, TestData.Now, TestData.SystemUser);
        quota.Reserve(TestData.Now, true, [], TestData.SystemUser);
        quotas.Setup(x => x.FindAsync(TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(quota);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.EnabledIssuer());

        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, quotas.Object, Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), Mock.Of<IManagedFiscalProfileRepository>());

        // Act
        var issuer = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((true, 100L, 1L, 99L), (issuer!.CanIssue, issuer.Quota.Limit, issuer.Quota.Used, issuer.Quota.Remaining));
    }

    [Fact]
    public async Task GetIssuer_ConfiguredAlerts_ReturnsThemWithTheQuota()
    {
        // Arrange
        var settings = Fakes.Options();
        settings.Quota.AlertPercentages = [100, 80, 80];
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(TestData.EnabledIssuer());

        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Clock(), Fakes.Options(settings), Mock.Of<IManagedFiscalProfileRepository>());

        // Act
        var issuer = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal([80, 100], issuer!.Quota.AlertPercentages);
    }
}
