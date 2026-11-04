using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetAllElectronicDocuments;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The electronic documents of the tenant of the session. They are issued by the services that originate them, through
/// gRPC; here they are read and retried.
/// </summary>
/// <remarks>
/// The XML and the PDF are references to ms-filestorage in each document (<c>xml</c>, <c>pdf</c>,
/// <c>attachedDocument</c>): they are downloaded there, with its permissions.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class ElectronicDocumentController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// The documents of the tenant, filtered and sorted by the criteria.
    /// </summary>
    /// <param name="criteria">Filter, order and page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page of documents.</returns>
    [HttpGet]
    public async Task<IActionResult> GetElectronicDocuments([FromQuery] C.Criteria criteria, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAllElectronicDocumentsQuery(criteria), cancellationToken);

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
        var result = await mediator.Send(new GetElectronicDocumentByIdQuery(id), cancellationToken);

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
        var result = await mediator.Send(new RetryElectronicDocumentCommand(id, null, null), cancellationToken);

        return Ok(result);
    }
}
