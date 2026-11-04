using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The numbering resolutions of the issuer of the tenant of the session.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class NumberingRangeController(IMediator mediator) : ControllerBase
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
        var ranges = await mediator.Send(new GetNumberingRangesQuery(documentType), cancellationToken);

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
        var id = await mediator.Send(command, cancellationToken);

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
        var id = await mediator.Send(command, cancellationToken);

        return Ok(id);
    }
}
