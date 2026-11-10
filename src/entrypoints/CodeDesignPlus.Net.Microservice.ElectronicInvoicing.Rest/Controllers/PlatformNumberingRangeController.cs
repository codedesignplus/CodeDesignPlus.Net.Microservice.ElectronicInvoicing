namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The numbering resolutions of the platform's own issuer: the same actions as <see cref="NumberingRangeController"/>,
/// without depending on the tenant of the session.
/// </summary>
/// <remarks>
/// Its own controller so the RBAC grants it apart, to whoever runs the platform.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class PlatformNumberingRangeController(IMediator mediator) : NumberingRangeControllerBase(mediator, true)
{
}
