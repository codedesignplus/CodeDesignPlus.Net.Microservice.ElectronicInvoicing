using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.TestProvider;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.TestProvider;

/// <summary>
/// A test provider never reaches a tax authority: only a tenant with the permission chooses it and issues with it, it is
/// never the default, the platform's issuer never uses it and it never goes to production.
/// </summary>
public class TestProviderGrantTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<ITestProviderGrantRepository> grants = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IOptions<ElectronicInvoicingOptions> options = Fakes.Options(Fakes.TestProviderOptions());

    public TestProviderGrantTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);

        foreach (var code in new[] { "Simulated", "Other" })
        {
            var provider = new Mock<IElectronicInvoiceProvider>();

            provider.Setup(x => x.Code).Returns(code);
            provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
            factory.Setup(x => x.Find(code)).Returns(provider.Object);
            factory.Setup(x => x.Get(code)).Returns(provider.Object);
        }

        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
        grants.EmulateOptimisticUpdate<ITestProviderGrantRepository, TestProviderGrantAggregate>();
    }

    private static RegisterIssuerCommand Register(string? provider) => new("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", "48", ["O-13"], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota", provider);

    /// <summary>
    /// A permission as Mongo gives it back: already saved, without pending events.
    /// </summary>
    private TestProviderGrantAggregate Stored(Guid tenant, bool granted = true)
    {
        var grant = TestProviderGrantAggregate.Grant(tenant, TestData.SystemUser, TestData.Now);

        if (!granted)
        {
            grant.Revoke(TestData.SystemUser, TestData.Now);
        }

        grant.GetAndClearEvents();
        typeof(TestProviderGrantAggregate).GetProperty(nameof(TestProviderGrantAggregate.Version))!.SetValue(grant, 1L);
        grants.Setup(x => x.FindAsync<TestProviderGrantAggregate>(grant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
        grants.Setup(x => x.ExistsAsync<TestProviderGrantAggregate>(grant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        return grant;
    }

    [Fact]
    public void AllowedProvider_TestProviderWithoutPermission_ThrowsTestProviderNotGranted()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerWrites.AllowedProvider(options.Value, factory.Object, "CO", "Simulated", false));

        // Assert
        Assert.Equal(AppErrors.TestProviderNotGranted.GetCode(), exception.Code);
    }

    [Fact]
    public void AllowedProvider_TestProviderWithPermission_ReturnsItsAdapter()
    {
        // Act
        var provider = IssuerWrites.AllowedProvider(options.Value, factory.Object, "CO", "simulated", true);

        // Assert
        Assert.Equal("Simulated", provider.Code);
    }

    [Fact]
    public void AllowedProvider_NoProviderWithPermission_TakesTheDefaultOfTheCountryNeverATestOne()
    {
        // Act
        var provider = IssuerWrites.AllowedProvider(options.Value, factory.Object, "CO", null, true);

        // Assert
        Assert.Equal("Other", provider.Code);
    }

    [Fact]
    public void AllowedProvider_CountryWithoutRoutingWithPermission_HasNoDefault()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerWrites.AllowedProvider(options.Value, factory.Object, "MX", null, true));

        // Assert
        Assert.Equal(AppErrors.ProviderNotSupported.GetCode(), exception.Code);
    }

    [Fact]
    public void AllowedProvider_TestProviderTheCountryRoutes_NeedsNoPermission()
    {
        // Arrange: as in Development, where the routing offers the simulated provider to everyone and first.
        var development = Fakes.Options();

        development.TestProviders = ["Simulated"];

        // Act
        var provider = IssuerWrites.AllowedProvider(development, factory.Object, "CO", null, false);

        // Assert
        Assert.Equal("Simulated", provider.Code);
    }

    [Fact]
    public async Task RegisterIssuer_TestProviderWithoutPermission_ThrowsTestProviderNotGranted()
    {
        // Arrange
        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, grants.Object, user.Object, options);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Register("Simulated"), CancellationToken.None));

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(AppErrors.TestProviderNotGranted.GetCode(), exception.Code);
    }

    [Fact]
    public async Task RegisterIssuer_TestProviderWithPermission_RegistersItWithTheTestProvider()
    {
        // Arrange
        Stored(TestData.Tenant);

        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, grants.Object, user.Object, options);

        // Act
        await handler.Handle(Register("Simulated"), CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.Provider == "Simulated" && issuer.Tenant == TestData.Tenant), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterIssuer_TestProviderWithPermissionRevoked_ThrowsTestProviderNotGranted()
    {
        // Arrange
        Stored(TestData.Tenant, granted: false);

        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, grants.Object, user.Object, options);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Register("Simulated"), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.TestProviderNotGranted.GetCode(), exception.Code);
    }

    [Fact]
    public async Task RegisterIssuer_PlatformIssuerWithTestProvider_ThrowsTestProviderNotGrantedWithoutReadingAnyPermission()
    {
        // Arrange: even a permission stored under the reserved tenant would not count.
        grants.Setup(x => x.FindAsync<TestProviderGrantAggregate>(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestProviderGrantAggregate.Grant(TestData.Tenant, TestData.User, TestData.Now));

        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, grants.Object, user.Object, options);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(Register("Simulated") with { PlatformIssuer = true }, CancellationToken.None));

        // Assert
        grants.Verify(x => x.FindAsync<TestProviderGrantAggregate>(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(AppErrors.TestProviderNotGranted.GetCode(), exception.Code);
    }

    [Fact]
    public async Task ChooseProvider_TestProviderWithoutPermission_ThrowsTestProviderNotGrantedAndKeepsTheProvider()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer("Other");

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new ChooseIssuerProviderCommandHandler(issuers.Object, factory.Object, grants.Object, pubsub.Object, user.Object, options);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new ChooseIssuerProviderCommand("Simulated"), CancellationToken.None));

        // Assert
        Assert.Equal((AppErrors.TestProviderNotGranted.GetCode(), "Other"), (exception.Code, issuer.Provider));
    }

    [Fact]
    public async Task ChooseProvider_TestProviderWithPermission_ChangesTheProvider()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer("Other");

        Stored(TestData.Tenant);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new ChooseIssuerProviderCommandHandler(issuers.Object, factory.Object, grants.Object, pubsub.Object, user.Object, options);

        // Act
        await handler.Handle(new ChooseIssuerProviderCommand("Simulated"), CancellationToken.None);

        // Assert
        Assert.Equal("Simulated", issuer.Provider);
    }

    [Fact]
    public async Task EnableIssuer_TestProviderToProduction_ThrowsTestProviderCannotGoToProduction()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new EnableIssuerCommandHandler(issuers.Object, pubsub.Object, user.Object, options);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new EnableIssuerCommand(IssuerEnvironment.Production), CancellationToken.None));

        // Assert
        Assert.Equal((DomainErrors.TestProviderCannotGoToProduction.GetCode(), IssuerEnvironment.Test, 0), (exception.Code, issuer.Environment, pubsub.Published().Count));
    }

    [Fact]
    public async Task EnableIssuer_RealProviderToProduction_GoesToProduction()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer("Other");

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new EnableIssuerCommandHandler(issuers.Object, pubsub.Object, user.Object, options);

        // Act
        await handler.Handle(new EnableIssuerCommand(IssuerEnvironment.Production), CancellationToken.None);

        // Assert
        Assert.Equal(IssuerEnvironment.Production, issuer.Environment);
    }

    [Fact]
    public async Task GetIssuer_TestProviderWithPermission_SaysItIsATestOneAndOffersIt()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        Stored(TestData.Tenant);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), grants.Object, user.Object, Fakes.Clock(), options);

        // Act
        var dto = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((true, true, "Other,Simulated"), (dto!.TestProvider, dto.CanIssue, string.Join(',', dto.AvailableProviders)));
    }

    [Fact]
    public async Task GetIssuer_TestProviderWithPermissionRevoked_CannotIssueAndIsNotOfferedIt()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        Stored(TestData.Tenant, granted: false);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), grants.Object, user.Object, Fakes.Clock(), options);

        // Act
        var dto = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((true, false, "Other"), (dto!.TestProvider, dto.CanIssue, string.Join(',', dto.AvailableProviders)));
    }

    [Fact]
    public async Task GetIssuer_RealProviderWithoutPermission_IsNotATestOneAndCanIssue()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer("Other");

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        var handler = new GetIssuerQueryHandler(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), grants.Object, user.Object, Fakes.Clock(), options);

        // Act
        var dto = await handler.Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal((false, true, "Other"), (dto!.TestProvider, dto.CanIssue, string.Join(',', dto.AvailableProviders)));
    }

    [Fact]
    public async Task Grant_TenantWithoutPermission_SavesItSignedByTheUserAndAnnouncesIt()
    {
        // Arrange
        TestProviderGrantAggregate? saved = null;

        grants
            .Setup(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<TestProviderGrantAggregate>>>(), It.IsAny<Func<TestProviderGrantAggregate, bool>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<TestProviderGrantAggregate>>, Func<TestProviderGrantAggregate, bool>, CancellationToken>(async (load, change, _) =>
            {
                saved = await load();

                return change(saved);
            });

        var handler = new GrantTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        await handler.Handle(new GrantTestProviderCommand(TestData.Tenant), CancellationToken.None);

        // Assert
        var granted = Assert.IsType<TestProviderGrantedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((true, TestData.User, TestData.Now, TestData.Tenant), (saved!.Granted, saved.GrantedBy, saved.GrantedAt, granted.Tenant));
    }

    [Fact]
    public async Task Grant_AlreadyGranted_EndsWellWithoutAnnouncingAgain()
    {
        // Arrange
        var grant = Stored(TestData.Tenant);
        var handler = new GrantTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        await handler.Handle(new GrantTestProviderCommand(TestData.Tenant), CancellationToken.None);

        // Assert
        Assert.Equal((TestData.SystemUser, 0), (grant.GrantedBy, pubsub.Published().Count));
    }

    [Fact]
    public async Task Grant_Revoked_GrantsItAgainAndAnnouncesIt()
    {
        // Arrange
        var grant = Stored(TestData.Tenant, granted: false);
        var handler = new GrantTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        await handler.Handle(new GrantTestProviderCommand(TestData.Tenant), CancellationToken.None);

        // Assert
        Assert.IsType<TestProviderGrantedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((true, TestData.User, (Guid?)null), (grant.Granted, grant.GrantedBy, grant.RevokedBy));
    }

    [Fact]
    public async Task Grant_TenantReservedForThePlatform_ThrowsTestProviderNotForThePlatform()
    {
        // Arrange
        var handler = new GrantTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new GrantTestProviderCommand(PlatformScope.Tenant), CancellationToken.None));

        // Assert
        Assert.Equal(DomainErrors.TestProviderNotForThePlatform.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Revoke_Granted_RevokesItSignedByTheUserAndAnnouncesIt()
    {
        // Arrange
        var grant = Stored(TestData.Tenant);
        var handler = new RevokeTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        await handler.Handle(new RevokeTestProviderCommand(TestData.Tenant), CancellationToken.None);

        // Assert
        var revoked = Assert.IsType<TestProviderRevokedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((false, (Guid?)TestData.User, (Instant?)TestData.Now, TestData.Tenant), (grant.Granted, grant.RevokedBy, grant.RevokedAt, revoked.Tenant));
    }

    [Fact]
    public async Task Revoke_TenantThatNeverHadIt_EndsWellWithoutWriting()
    {
        // Arrange
        var handler = new RevokeTestProviderCommandHandler(grants.Object, pubsub.Object, user.Object, Fakes.Clock());

        // Act
        await handler.Handle(new RevokeTestProviderCommand(TestData.Tenant), CancellationToken.None);

        // Assert
        grants.Verify(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<TestProviderGrantAggregate>>>(), It.IsAny<Func<TestProviderGrantAggregate, bool>>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Empty(pubsub.Published());
    }

    [Fact]
    public async Task GetGrant_TenantThatNeverHadIt_SaysNotGrantedAndListsTheTestProviders()
    {
        // Arrange
        var handler = new GetTestProviderGrantQueryHandler(grants.Object, options);

        // Act
        var dto = await handler.Handle(new GetTestProviderGrantQuery(TestData.Tenant), CancellationToken.None);

        // Assert
        Assert.Equal((TestData.Tenant, false, (Guid?)null, "Simulated"), (dto.Tenant, dto.Granted, dto.GrantedBy, string.Join(',', dto.TestProviders)));
    }

    [Fact]
    public async Task GetGrant_Revoked_SaysWhoGrantedAndWhoRevokedIt()
    {
        // Arrange
        Stored(TestData.Tenant, granted: false);

        var handler = new GetTestProviderGrantQueryHandler(grants.Object, options);

        // Act
        var dto = await handler.Handle(new GetTestProviderGrantQuery(TestData.Tenant), CancellationToken.None);

        // Assert
        Assert.Equal((false, (Guid?)TestData.SystemUser, (Guid?)TestData.SystemUser, (Instant?)TestData.Now), (dto.Granted, dto.GrantedBy, dto.RevokedBy, dto.RevokedAt));
    }

    [Fact]
    public void Validate_TestProvidersWithAnEmptyCode_SaysSo()
    {
        // Arrange
        var settings = Fakes.TestProviderOptions();

        settings.TestProviders = ["Simulated", " "];

        // Act
        var problems = settings.Validate(new ValidationContext(settings)).ToList();

        // Assert
        Assert.Contains(problems, x => x.MemberNames.Contains(nameof(ElectronicInvoicingOptions.TestProviders)));
    }
}
