using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessProviderEvent;
using Microsoft.AspNetCore.Authorization;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// Where the providers call with the result of the documents.
/// </summary>
/// <param name="mediator">Mediator that sends the commands.</param>
[Route("api/[controller]")]
[ApiController]
public class ProviderEventsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Receives a call of a provider.
    /// </summary>
    /// <remarks>
    /// Anonymous: the call is authenticated by the signature of the provider, with the header, the algorithm and the
    /// secret of its configuration. While the provider's updates are asked for (<c>StatusUpdates = Poll</c>, the default)
    /// every call is ignored. A call that cannot be trusted is discarded and logged, and still answered 200 so the
    /// provider stops retrying it; the call is only a hint, and the state of the document is asked to the provider. When
    /// the provider or the Vault does not answer, it is answered 503 so the provider retries; the status job recovers
    /// what is left.
    /// </remarks>
    /// <param name="provider">The code of the provider (for example <c>aliaddo</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>200, or 503 when a dependency did not answer.</returns>
    [HttpPost("{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);

        var body = await reader.ReadToEndAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        try
        {
            await mediator.Send(new ProcessProviderEventCommand(provider, body, headers), cancellationToken);
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsTransient(exception))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok();
    }
}
