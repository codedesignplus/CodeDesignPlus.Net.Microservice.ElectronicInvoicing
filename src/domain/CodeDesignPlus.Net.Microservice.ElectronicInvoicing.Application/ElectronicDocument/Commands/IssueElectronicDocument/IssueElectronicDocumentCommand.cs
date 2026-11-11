namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;

/// <summary>
/// Issues an electronic document: it is saved waiting to be sent and the result arrives by event.
/// </summary>
/// <param name="Id">The id the caller derived from its own source; repeating it with the same content returns the same document.</param>
/// <param name="Tenant">The tenant that issues; null for the tenant of the user. Empty when <paramref name="PlatformIssuer"/> is set.</param>
/// <param name="PlatformIssuer">Whether the platform's issuer issues it; naming a tenant as well is refused (error 218).</param>
/// <param name="Type">The type of the document.</param>
/// <param name="Reference">The caller's opaque reference, which comes back in the events.</param>
/// <param name="Content">What the document says.</param>
/// <param name="PrecededBy">A document that must be validated before this one is sent.</param>
public record IssueElectronicDocumentCommand(Guid Id, Guid? Tenant, bool PlatformIssuer, ElectronicDocumentType Type, string Reference, DocumentContentInput Content, Guid? PrecededBy) : IRequest<IssuedDocumentDto>
{
    /// <summary>
    /// The prefix of the number the caller gives, with <see cref="Consecutive"/>; null for this service to number it.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// The consecutive of the number the caller gives, with <see cref="Prefix"/>; null for this service to number it.
    /// </summary>
    public long? Consecutive { get; init; }
}

/// <summary>
/// Validates <see cref="IssueElectronicDocumentCommand"/>.
/// </summary>
public class Validator : AbstractValidator<IssueElectronicDocumentCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(FieldLength.Title);
        RuleFor(x => x.Content).NotNull().SetValidator(new DocumentContentInputValidator());
    }
}
