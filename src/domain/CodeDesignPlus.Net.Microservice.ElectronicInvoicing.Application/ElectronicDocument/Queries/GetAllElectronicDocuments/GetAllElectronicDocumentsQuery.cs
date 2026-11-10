namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetAllElectronicDocuments;

/// <summary>
/// The electronic documents of the tenant of the user (or of the platform's issuer), filtered and sorted by the criteria.
/// </summary>
/// <param name="Criteria">Filter, order and page.</param>
public record GetAllElectronicDocumentsQuery(C.Criteria Criteria) : IRequest<Pagination<ElectronicDocumentDto>>
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Handles <see cref="GetAllElectronicDocumentsQuery"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="user">The context of the user who asks.</param>
public class GetAllElectronicDocumentsQueryHandler(IElectronicDocumentRepository repository, IUserContext user) : IRequestHandler<GetAllElectronicDocumentsQuery, Pagination<ElectronicDocumentDto>>
{
    /// <summary>
    /// Reads the page of documents inside the tenant of the user.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<Pagination<ElectronicDocumentDto>> Handle(GetAllElectronicDocumentsQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);
        var page = await repository.MatchingAsync<ElectronicDocumentAggregate>(request.Criteria ?? new C.Criteria(), tenant, cancellationToken);

        return new Pagination<ElectronicDocumentDto>(page.Data.Select(ElectronicDocumentDto.From), page.TotalCount, page.Limit, page.Skip);
    }
}
