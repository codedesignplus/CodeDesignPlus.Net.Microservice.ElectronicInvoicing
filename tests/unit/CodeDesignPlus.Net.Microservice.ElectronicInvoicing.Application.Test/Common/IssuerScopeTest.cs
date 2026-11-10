using CodeDesignPlus.Net.Core.Abstractions.Models.Pager;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetAllElectronicDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;
using C = CodeDesignPlus.Net.Core.Abstractions.Models.Criteria;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Common;

/// <summary>
/// Which issuer a request works with: the platform's own or a tenant's, never a guess between the two.
/// </summary>
public class IssuerScopeTest
{
    private readonly Mock<IUserContext> user = new();

    public IssuerScopeTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
    }

    [Fact]
    public void TenantOf_PlatformIssuer_IsTheReservedTenantWhateverTheUser()
    {
        // Act
        var tenant = IssuerScope.TenantOf(null, true, user.Object);

        // Assert
        Assert.Equal(PlatformScope.Tenant, tenant);
    }

    [Fact]
    public void TenantOf_NoTenantNamed_IsTheTenantOfTheUser()
    {
        // Act
        var tenant = IssuerScope.TenantOf(null, false, user.Object);

        // Assert
        Assert.Equal(TestData.Tenant, tenant);
    }

    [Fact]
    public void TenantOf_TenantNamed_IsThatTenantAndNotTheUsers()
    {
        // Arrange
        var named = Guid.NewGuid();

        // Act
        var tenant = IssuerScope.TenantOf(named, false, user.Object);

        // Assert
        Assert.Equal(named, tenant);
    }

    [Fact]
    public void TenantOf_PlatformIssuerAndTenant_ThrowsPlatformIssuerWithTenant()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerScope.TenantOf(TestData.Tenant, true, user.Object));

        // Assert
        Assert.Equal(AppErrors.PlatformIssuerWithTenant.GetCode(), exception.Code);
    }

    [Fact]
    public void TenantOf_ReservedTenantNamed_ThrowsTenantIsReserved()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerScope.TenantOf(PlatformScope.Tenant, false, user.Object));

        // Assert
        Assert.Equal(AppErrors.TenantIsReserved.GetCode(), exception.Code);
    }

    [Fact]
    public void TenantOf_UserInTheReservedTenant_ThrowsTenantIsReserved()
    {
        // Arrange
        user.Setup(x => x.Tenant).Returns(PlatformScope.Tenant);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerScope.TenantOf(false, user.Object));

        // Assert
        Assert.Equal(AppErrors.TenantIsReserved.GetCode(), exception.Code);
    }

    [Fact]
    public void TenantOf_NoTenantAnywhere_ThrowsTenantIsRequired()
    {
        // Arrange
        user.Setup(x => x.Tenant).Returns(Guid.Empty);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerScope.TenantOf(false, user.Object));

        // Assert
        Assert.Equal(DomainErrors.TenantIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public async Task RegisterIssuer_PlatformIssuer_RegistersItUnderTheReservedTenantAndNotTheUsers()
    {
        // Arrange
        var issuers = new Mock<IIssuerRepository>();
        var factory = new Mock<IElectronicInvoiceProviderFactory>();
        var provider = new Mock<IElectronicInvoiceProvider>();

        provider.Setup(x => x.Code).Returns("Simulated");
        factory.Setup(x => x.Find("Simulated")).Returns(provider.Object);

        var handler = new RegisterIssuerCommandHandler(issuers.Object, factory.Object, user.Object, Fakes.Options(Fakes.Options()));
        var command = new RegisterIssuerCommand("CO", "31", "900000002-1", "Plataforma SAS", "1", "48", ["O-13"], TestData.Address(), "facturas@example.com", "3000000001", "America/Bogota", null) { PlatformIssuer = true };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        issuers.Verify(x => x.CreateIfAbsentAsync(It.Is<IssuerAggregate>(issuer => issuer.Tenant == PlatformScope.Tenant && issuer.Id == IssuerAggregate.IdFor(PlatformScope.Tenant)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllElectronicDocuments_PlatformIssuer_ReadsOnlyTheDocumentsOfTheReservedTenant()
    {
        // Arrange
        var repository = new Mock<IElectronicDocumentRepository>();

        repository.Setup(x => x.MatchingAsync<ElectronicDocumentAggregate>(It.IsAny<C.Criteria>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Pagination<ElectronicDocumentAggregate>([], 0, 10, 0));

        var handler = new GetAllElectronicDocumentsQueryHandler(repository.Object, user.Object);

        // Act
        await handler.Handle(new GetAllElectronicDocumentsQuery(new C.Criteria()) { PlatformIssuer = true }, CancellationToken.None);

        // Assert
        repository.Verify(x => x.MatchingAsync<ElectronicDocumentAggregate>(It.IsAny<C.Criteria>(), PlatformScope.Tenant, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ProviderPlanUsage_Started_BelongsToThePlatformsScope()
    {
        // Act
        var usage = ProviderPlanUsageAggregate.Start("Simulated", 40_000, TestData.Now, TestData.Now + Duration.FromDays(365), TestData.SystemUser);

        // Assert
        Assert.Equal(PlatformScope.Tenant, usage.Tenant);
    }

    [Fact]
    public void PlatformScopeTenant_Shape_IsNoGuidARealTenantCanHave()
    {
        // Arrange: a tenant id is a version 4 (or 7) GUID with the RFC variant; the reserved one has neither.
        var text = PlatformScope.Tenant.ToString();

        // Act
        var shape = (Version: text[14], Variant: text[19]);

        // Assert
        Assert.Equal(('0', '0'), shape);
    }
}
