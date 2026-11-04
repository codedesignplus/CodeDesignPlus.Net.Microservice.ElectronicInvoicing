namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;

/// <summary>
/// Handles <see cref="GetElectronicDocumentByIdQuery"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="user">The context of the user who asks.</param>
/// <param name="mapper">The mapper to the DTO.</param>
public class GetElectronicDocumentByIdQueryHandler(IElectronicDocumentRepository repository, IUserContext user, IMapper mapper) : IRequestHandler<GetElectronicDocumentByIdQuery, ElectronicDocumentDto>
{
    /// <summary>
    /// Finds the document inside the tenant of the user.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    public async Task<ElectronicDocumentDto> Handle(GetElectronicDocumentByIdQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var document = await repository.FindAsync<ElectronicDocumentAggregate>(request.Id, user.Tenant, cancellationToken);

        ApplicationGuard.IsNull(document, Errors.ElectronicDocumentNotFound);

        return mapper.Map<ElectronicDocumentDto>(document);
    }
}
