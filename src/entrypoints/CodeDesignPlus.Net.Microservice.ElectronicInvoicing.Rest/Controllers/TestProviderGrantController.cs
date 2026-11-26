using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.TestProvider;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The permission of a tenant to issue with a test provider (an adapter that never reaches a tax authority): whoever
/// runs the platform reads it, grants it and revokes it, naming the tenant by its id.
/// </summary>
/// <remarks>
/// Its own controller so the RBAC grants it apart, to whoever runs the platform: no tenant may give itself the
/// permission. It does not depend on the tenant of the session, and this service does not check roles.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class TestProviderGrantController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// The permission of a tenant as it stands now.
    /// </summary>
    /// <param name="tenant">The id of the tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>200 with the permission; not granted when the tenant never had one.</returns>
    [HttpGet("{tenant:guid}")]
    [ProducesResponseType<TestProviderGrantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGrant(Guid tenant, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetTestProviderGrantQuery(tenant), cancellationToken));
    }

    /// <summary>
    /// Lets the tenant issue with a test provider; granting it again ends well without repeating.
    /// </summary>
    /// <param name="tenant">The id of the tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPut("{tenant:guid}")]
    public async Task<IActionResult> Grant(Guid tenant, CancellationToken cancellationToken)
    {
        await mediator.Send(new GrantTestProviderCommand(tenant), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Takes the permission from the tenant: its issuer stops issuing with a test provider, and what it already issued
    /// stays as it is.
    /// </summary>
    /// <param name="tenant">The id of the tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpDelete("{tenant:guid}")]
    public async Task<IActionResult> Revoke(Guid tenant, CancellationToken cancellationToken)
    {
        await mediator.Send(new RevokeTestProviderCommand(tenant), cancellationToken);

        return NoContent();
    }
}
