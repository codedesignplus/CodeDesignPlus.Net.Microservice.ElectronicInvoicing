namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The platform's own issuer (the company that runs the platform and invoices what it sells): the same actions as
/// <see cref="IssuerController"/>, without depending on the tenant of the session.
/// </summary>
/// <remarks>
/// Its own controller so the RBAC grants it apart, to whoever runs the platform.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class PlatformIssuerController(IMediator mediator) : IssuerControllerBase(mediator, true)
{
}
