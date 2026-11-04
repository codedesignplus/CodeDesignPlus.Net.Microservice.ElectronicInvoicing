namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;

/// <summary>
/// Gets an electronic document of the tenant of the user by its id.
/// </summary>
/// <param name="Id">The id of the document.</param>
public record GetElectronicDocumentByIdQuery(Guid Id) : IRequest<ElectronicDocumentDto>;

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
