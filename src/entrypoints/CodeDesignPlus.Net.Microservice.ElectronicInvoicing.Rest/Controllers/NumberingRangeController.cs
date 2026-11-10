namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The numbering resolutions of the issuer of the tenant of the session.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class NumberingRangeController(IMediator mediator) : NumberingRangeControllerBase(mediator, false)
{
}
