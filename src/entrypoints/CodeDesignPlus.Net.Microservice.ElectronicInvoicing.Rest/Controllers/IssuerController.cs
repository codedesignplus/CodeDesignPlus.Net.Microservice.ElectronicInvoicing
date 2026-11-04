using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.UpdateIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The issuer of the tenant of the session: its fiscal data, its provider, its mandate and its enablement.
/// </summary>
/// <remarks>
/// Who may use each action is decided by the RBAC of the screen; this service does not check roles.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class IssuerController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// The issuer of the tenant, with what its provider can do and its quota of documents.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>200 with the issuer, or 204 when the tenant has none registered.</returns>
    [HttpGet]
    public async Task<IActionResult> GetIssuer(CancellationToken cancellationToken)
    {
        var issuer = await mediator.Send(new GetIssuerQuery(), cancellationToken);

        return issuer is null ? NoContent() : Ok(issuer);
    }

    /// <summary>
    /// Registers the issuer of the tenant; a second registration ends well without repeating.
    /// </summary>
    /// <param name="command">The fiscal data and the provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost]
    public async Task<IActionResult> RegisterIssuer([FromBody] RegisterIssuerCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Replaces the fiscal data of the issuer.
    /// </summary>
    /// <param name="command">The fiscal data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPut]
    public async Task<IActionResult> UpdateIssuer([FromBody] UpdateIssuerCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Changes the provider, before the issuer exists in one.
    /// </summary>
    /// <param name="command">The provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPut("provider")]
    public async Task<IActionResult> ChooseProvider([FromBody] ChooseIssuerProviderCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Records the mandate accepted by the user of the session.
    /// </summary>
    /// <param name="command">The version and the signed document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("mandate")]
    public async Task<IActionResult> AcceptMandate([FromBody] AcceptIssuerMandateCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Connects the credentials of an issuer whose provider works with each issuer's own account.
    /// </summary>
    /// <param name="command">The credentials and the settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPut("credentials")]
    public async Task<IActionResult> ConnectCredentials([FromBody] ConnectIssuerCredentialsCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Creates the issuer in its provider.
    /// </summary>
    /// <param name="command">The person the provider registers as its user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision([FromBody] ProvisionIssuerCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Starts the enablement tests with the test set of the tax authority.
    /// </summary>
    /// <param name="command">The test set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("testing")]
    public async Task<IActionResult> StartTesting([FromBody] StartIssuerTestingCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Enables the issuer to send to an environment.
    /// </summary>
    /// <param name="command">The environment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("enable")]
    public async Task<IActionResult> Enable([FromBody] EnableIssuerCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Suspends the issuer.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204.</returns>
    [HttpPost("suspend")]
    public async Task<IActionResult> Suspend(CancellationToken cancellationToken)
    {
        await mediator.Send(new SuspendIssuerCommand(), cancellationToken);

        return NoContent();
    }
}
