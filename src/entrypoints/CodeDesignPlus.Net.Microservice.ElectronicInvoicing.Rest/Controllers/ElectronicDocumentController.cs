namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// Electronic documents of the tenant of the user.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
/// <param name="mapper">Mapper from the DTOs to the commands.</param>
[Route("api/[controller]")]
[ApiController]
public class ElectronicDocumentController(IMediator mediator, IMapper mapper) : ControllerBase
{
    /// <summary>
    /// Gets an electronic document by its id.
    /// </summary>
    /// <param name="id">The id of the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetElectronicDocumentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetElectronicDocumentByIdQuery(id), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Creates an electronic document.
    /// </summary>
    /// <param name="data">The data of the document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>HTTP status code 204 (No Content).</returns>
    [HttpPost]
    public async Task<IActionResult> CreateElectronicDocument([FromBody] CreateElectronicDocumentDto data, CancellationToken cancellationToken)
    {
        await mediator.Send(mapper.Map<CreateElectronicDocumentCommand>(data), cancellationToken);

        return NoContent();
    }
}
