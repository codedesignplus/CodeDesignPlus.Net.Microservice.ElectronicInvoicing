using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The actions on the numbering resolutions of the issuer of a scope: the tenant of the session or the platform's issuer.
/// </summary>
/// <remarks>
/// The scope is set here on every request, whatever the body says, so a tenant's endpoint can never reach the
/// platform's issuer. Who may use each action is decided by the RBAC of the screen, by controller.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
/// <param name="platformIssuer">Whether the controller works with the platform's issuer.</param>
public abstract class NumberingRangeControllerBase(IMediator mediator, bool platformIssuer) : ControllerBase
{
    /// <summary>
    /// The ranges of the issuer.
    /// </summary>
    /// <param name="documentType">Only the ranges of this type; all when it is missing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranges, with the numbers left.</returns>
    [HttpGet]
    public async Task<IActionResult> GetNumberingRanges([FromQuery] ElectronicDocumentType? documentType, CancellationToken cancellationToken)
    {
        var ranges = await mediator.Send(new GetNumberingRangesQuery(documentType) { PlatformIssuer = platformIssuer }, cancellationToken);

        return Ok(ranges);
    }

    /// <summary>
    /// Registers a resolution by hand; registering it again ends well and keeps its next number.
    /// </summary>
    /// <param name="command">The resolution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the range.</returns>
    [HttpPost]
    public async Task<IActionResult> RegisterNumberingRange([FromBody] RegisterNumberingRangeCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command with { PlatformIssuer = platformIssuer }, cancellationToken);

        return Ok(id);
    }

    /// <summary>
    /// Brings from the provider the resolution of a prefix, for a type of document.
    /// </summary>
    /// <param name="command">The type and the prefix.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the range.</returns>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncNumberingRange([FromBody] SyncNumberingRangeCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command with { PlatformIssuer = platformIssuer }, cancellationToken);

        return Ok(id);
    }
}
