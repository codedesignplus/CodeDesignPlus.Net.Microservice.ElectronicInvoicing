namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.CreateElectronicDocument;

/// <summary>
/// Creates an electronic document for the tenant of the user.
/// </summary>
/// <param name="Id">The id the caller derived from its own source.</param>
/// <param name="Reference">The caller's opaque reference.</param>
[DtoGenerator]
public record CreateElectronicDocumentCommand(Guid Id, string Reference) : IRequest;

/// <summary>
/// Validates <see cref="CreateElectronicDocumentCommand"/>.
/// </summary>
public class Validator : AbstractValidator<CreateElectronicDocumentCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(FieldLength.Title);
    }
}
