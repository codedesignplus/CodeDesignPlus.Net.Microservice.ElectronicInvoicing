using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Repositories;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using CodeDesignPlus.Net.PubSub.Abstractions;
using CodeDesignPlus.Net.Security.Abstractions;
using DomainErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Simulated;

/// <summary>
/// The steps the screen of the issuer takes, in order, for a tenant with the permission to use the simulated provider:
/// register with it, accept the mandate, optionally create it in the provider, and enable it without a test set.
/// </summary>
public class SimulatedIssuerEnablementTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<ITestProviderGrantRepository> grants = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IClock> clock = new();
    private readonly IElectronicInvoiceProviderFactory factory = new ElectronicInvoiceProviderFactory([SimulatedProviders.Create()]);
    private readonly IOptions<ElectronicInvoicingOptions> options = Microsoft.Extensions.Options.Options.Create(new ElectronicInvoicingOptions
    {
        Routing = new() { ["CO"] = ["Alanube"] },
        TestProviders = [SimulatedProvider.ProviderCode],
        CheckDigitDocumentTypes = [.. TestData.CheckDigitRules().DocumentTypes],
        CheckDigitWeights = [.. TestData.CheckDigitRules().Weights]
    });

    private IssuerAggregate? stored;

    public SimulatedIssuerEnablementTest()
    {
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, TestData.SystemUser, TestData.Now);

        grants.Setup(x => x.FindAsync<TestProviderGrantAggregate>(grant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        clock.Setup(x => x.GetCurrentInstant()).Returns(TestData.Now);

        issuers.Setup(x => x.ExistsAsync<IssuerAggregate>(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => stored is not null);
        issuers.Setup(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>())).Callback<IssuerAggregate, CancellationToken>((issuer, _) => stored = issuer).ReturnsAsync(true);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(() => stored!);
        issuers
            .Setup(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<IssuerAggregate>>>(), It.IsAny<Func<IssuerAggregate, bool>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<IssuerAggregate>>, Func<IssuerAggregate, bool>, CancellationToken>(async (load, change, _) => change(await load()));
    }

    private Task Register() => new RegisterIssuerCommandHandler(issuers.Object, factory, grants.Object, user.Object, options).Handle(new RegisterIssuerCommand("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", "48", ["R-99-PN"], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota", SimulatedProvider.ProviderCode), CancellationToken.None);

    private Task AcceptMandate() => new AcceptIssuerMandateCommandHandler(issuers.Object, Mock.Of<IPubSub>(), Mock.Of<IIssuingAvailabilityAnnouncer>(), user.Object, clock.Object).Handle(new AcceptIssuerMandateCommand("1.0", null), CancellationToken.None);

    private Task Provision() => new ProvisionIssuerCommandHandler(issuers.Object, factory, Mock.Of<IPubSub>(), Mock.Of<IIssuingAvailabilityAnnouncer>(), user.Object).Handle(new ProvisionIssuerCommand("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

    private Task Enable(IssuerEnvironment environment) => new EnableIssuerCommandHandler(issuers.Object, Mock.Of<IPubSub>(), Mock.Of<IIssuingAvailabilityAnnouncer>(), user.Object, options).Handle(new EnableIssuerCommand(environment), CancellationToken.None);

    [Fact]
    public async Task Enable_RegisteredWithTheMandateAndProvisioned_CanIssue()
    {
        // Arrange
        await Register();
        await AcceptMandate();
        await Provision();

        // Act
        await Enable(IssuerEnvironment.Test);

        // Assert
        Assert.Equal((SimulatedProvider.ProviderCode, IssuerStatus.Enabled, true, true), (stored!.Provider, stored.Status, stored.ProviderIssuerId is not null, stored.CanIssue));
    }

    [Fact]
    public async Task Enable_WithoutProvisioningNorTestSet_CanIssue()
    {
        // Arrange: with the test provider the enablement is reduced to enabling.
        await Register();
        await AcceptMandate();

        // Act
        await Enable(IssuerEnvironment.Test);

        // Assert
        Assert.True(stored!.CanIssue);
    }

    [Fact]
    public async Task Enable_ToProduction_ThrowsTestProviderCannotGoToProduction()
    {
        // Arrange
        await Register();
        await AcceptMandate();

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Enable(IssuerEnvironment.Production));

        // Assert
        Assert.Equal(DomainErrors.TestProviderCannotGoToProduction.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Enable_WithoutTheMandate_ThrowsMandateNotAccepted()
    {
        // Arrange
        await Register();

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Enable(IssuerEnvironment.Test));

        // Assert
        Assert.Equal(DomainErrors.MandateNotAccepted.GetCode(), exception.Code);
    }
}
