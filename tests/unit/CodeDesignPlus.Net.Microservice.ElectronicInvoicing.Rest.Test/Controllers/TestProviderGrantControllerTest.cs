using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.TestProvider;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Test.Controllers;

/// <summary>
/// The permission to issue with a test provider is of the tenant named in the route, never of the tenant of the session.
/// </summary>
public class TestProviderGrantControllerTest
{
    private static readonly Guid Tenant = Guid.Parse("aa000000-0000-4000-8000-0000000000b1");

    private readonly Mock<IMediator> mediator = new();

    [Fact]
    public async Task GetGrant_Tenant_ReturnsItsPermission()
    {
        // Arrange
        var dto = new TestProviderGrantDto { Id = Guid.NewGuid(), Tenant = Tenant, Granted = true };

        mediator.Setup(x => x.Send(It.Is<GetTestProviderGrantQuery>(q => q.Tenant == Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        // Act
        var result = await new TestProviderGrantController(mediator.Object).GetGrant(Tenant, CancellationToken.None);

        // Assert
        Assert.Same(dto, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Grant_Tenant_SendsTheGrantOfThatTenant()
    {
        // Act
        var result = await new TestProviderGrantController(mediator.Object).Grant(Tenant, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<GrantTestProviderCommand>(c => c.Tenant == Tenant), It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Revoke_Tenant_SendsTheRevocationOfThatTenant()
    {
        // Act
        var result = await new TestProviderGrantController(mediator.Object).Revoke(Tenant, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<RevokeTestProviderCommand>(c => c.Tenant == Tenant), It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsType<NoContentResult>(result);
    }
}
