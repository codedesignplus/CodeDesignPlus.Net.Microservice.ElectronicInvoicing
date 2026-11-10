namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.UpdateIssuer;

/// <summary>
/// Replaces the fiscal data of the issuer of the tenant of the user.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country.</param>
/// <param name="TaxIdTypeCode">Code of the type of identification in the tax authority's list.</param>
/// <param name="TaxId">Tax id with its check digit after a hyphen.</param>
/// <param name="LegalName">Legal name.</param>
/// <param name="PersonTypeCode">Code of the type of person.</param>
/// <param name="TaxRegimeCode">Code of the tax regime.</param>
/// <param name="TaxResponsibilities">Codes of the fiscal responsibilities.</param>
/// <param name="Address">The address.</param>
/// <param name="Email">The email.</param>
/// <param name="Phone">The phone.</param>
/// <param name="TimeZoneId">IANA time zone of the issuer.</param>
public record UpdateIssuerCommand(string Country, string TaxIdTypeCode, string TaxId, string LegalName, string PersonTypeCode, string TaxRegimeCode, List<string> TaxResponsibilities, PostalAddress Address, string Email, string Phone, string TimeZoneId) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="UpdateIssuerCommand"/>.
/// </summary>
public class Validator : AbstractValidator<UpdateIssuerCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.TaxIdTypeCode).NotEmpty();
        RuleFor(x => x.TaxId).NotEmpty();
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.Address).NotNull();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.TimeZoneId).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="UpdateIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
public class UpdateIssuerCommandHandler(IIssuerRepository issuers, IPubSub pubsub, IUserContext user) : IRequestHandler<UpdateIssuerCommand>
{
    /// <summary>
    /// Replaces the fiscal data.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task Handle(UpdateIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var data = new IssuerFiscalData(request.Country, request.TaxIdTypeCode, request.TaxId, request.LegalName, request.PersonTypeCode, request.TaxRegimeCode, request.TaxResponsibilities ?? [], request.Address, request.Email, request.Phone, request.TimeZoneId);

        return IssuerWrites.UpdateAsync(issuers, pubsub, tenant, x => x.UpdateFiscalData(data, user.IdUser), cancellationToken);
    }
}
