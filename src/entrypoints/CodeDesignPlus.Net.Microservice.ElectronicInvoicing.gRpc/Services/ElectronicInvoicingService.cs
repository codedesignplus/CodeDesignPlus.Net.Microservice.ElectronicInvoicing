namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Services;

/// <summary>
/// The electronic invoicing contract other services call inside the cluster.
/// </summary>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
/// <param name="mapper">Mapper between the messages and the DTOs.</param>
public class ElectronicInvoicingService(IMediator mediator, IMapper mapper) : ElectronicInvoicing.ElectronicInvoicingBase
{
    /// <summary>
    /// Gets an electronic document of the tenant of the caller.
    /// </summary>
    /// <param name="request">The id of the document.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The document.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown when the id is not a valid identifier.</exception>
    public override async Task<GetDocumentResponse> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        InfrastructureGuard.IsFalse(Guid.TryParse(request.Id, out var id), Infrastructure.Errors.InvalidId);

        var document = await mediator.Send(new GetElectronicDocumentByIdQuery(id), context.CancellationToken);

        return mapper.Map<GetDocumentResponse>(document);
    }
}
