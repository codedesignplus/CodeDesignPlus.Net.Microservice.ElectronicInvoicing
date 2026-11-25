using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The issuer of the tenant of the session: its fiscal data, its provider, its mandate and its enablement.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class IssuerController(IMediator mediator) : IssuerControllerBase(mediator, false)
{
    private readonly IMediator mediator = mediator;

    /// <summary>
    /// Aligns the issuer now with the tax regime and the fiscal responsibilities it must follow, in its provider first.
    /// </summary>
    /// <remarks>
    /// Only the issuer of a tenant follows fiscal data managed from outside it; the platform's issuer edits its own.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("sync-fiscal-profile")]
    public async Task<IActionResult> SyncFiscalProfile(CancellationToken cancellationToken)
    {
        await mediator.Send(new SyncIssuerFiscalProfileCommand(), cancellationToken);

        return NoContent();
    }
}
