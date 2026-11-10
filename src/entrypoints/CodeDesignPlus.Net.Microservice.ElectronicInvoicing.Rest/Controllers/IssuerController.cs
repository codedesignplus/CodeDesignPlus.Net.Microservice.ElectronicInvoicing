namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The issuer of the tenant of the session: its fiscal data, its provider, its mandate and its enablement.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class IssuerController(IMediator mediator) : IssuerControllerBase(mediator, false)
{
}
