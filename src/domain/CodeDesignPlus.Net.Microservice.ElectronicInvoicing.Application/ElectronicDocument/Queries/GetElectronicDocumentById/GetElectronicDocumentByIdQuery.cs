namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;

/// <summary>
/// Gets an electronic document of a tenant by its id.
/// </summary>
/// <param name="Id">The id of the document.</param>
/// <param name="Tenant">The tenant that issued it; null for the tenant of the user.</param>
public record GetElectronicDocumentByIdQuery(Guid Id, Guid? Tenant = null) : IRequest<ElectronicDocumentDto>;

/// <summary>
/// Validates <see cref="GetElectronicDocumentByIdQuery"/>.
/// </summary>
public class Validator : AbstractValidator<GetElectronicDocumentByIdQuery>
{
    /// <summary>
    /// Declares the rules of the query.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="GetElectronicDocumentByIdQuery"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="user">The context of the user who asks.</param>
public class GetElectronicDocumentByIdQueryHandler(IElectronicDocumentRepository repository, IUserContext user) : IRequestHandler<GetElectronicDocumentByIdQuery, ElectronicDocumentDto>
{
    /// <summary>
    /// Finds the document inside the tenant.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    public async Task<ElectronicDocumentDto> Handle(GetElectronicDocumentByIdQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = request.Tenant is { } named && named != Guid.Empty ? named : user.Tenant;

        ApplicationGuard.GuidIsEmpty(tenant, Errors.InvalidRequest);

        var document = await repository.FindAsync<ElectronicDocumentAggregate>(request.Id, tenant, cancellationToken);

        ApplicationGuard.IsNull(document, Errors.ElectronicDocumentNotFound);

        return ElectronicDocumentDto.From(document);
    }
}
