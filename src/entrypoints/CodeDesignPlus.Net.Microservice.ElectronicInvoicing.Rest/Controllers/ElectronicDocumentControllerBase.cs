using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetAllElectronicDocuments;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The actions on the electronic documents of a scope: the tenant of the session or the platform's issuer. They are
/// issued by the services that originate them, through gRPC; here they are read and retried.
/// </summary>
/// <remarks>
/// The scope is set here on every request, whatever the body says, so a tenant's endpoint can never reach the
/// platform's issuer. Who may use each action is decided by the RBAC of the screen, by controller.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
/// <param name="platformIssuer">Whether the controller works with the platform's issuer.</param>
public abstract class ElectronicDocumentControllerBase(IMediator mediator, bool platformIssuer) : ControllerBase
{
    /// <summary>
    /// The documents of the scope, filtered and sorted by the criteria.
    /// </summary>
    /// <param name="criteria">Filter, order and page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page of documents.</returns>
    [HttpGet]
    public async Task<IActionResult> GetElectronicDocuments([FromQuery] C.Criteria criteria, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAllElectronicDocumentsQuery(criteria) { PlatformIssuer = platformIssuer }, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// A document by its id.
    /// </summary>
    /// <param name="id">The id of the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetElectronicDocumentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetElectronicDocumentByIdQuery(id, null, platformIssuer), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Looks at a waiting document now, and restarts its retries if it was stalled. A rejected document is corrected by
    /// the service that originated it, which knows its content.
    /// </summary>
    /// <param name="id">The id of the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document as it stands.</returns>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> RetryElectronicDocument(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RetryElectronicDocumentCommand(id, null, null, platformIssuer), cancellationToken);

        return Ok(result);
    }
}
