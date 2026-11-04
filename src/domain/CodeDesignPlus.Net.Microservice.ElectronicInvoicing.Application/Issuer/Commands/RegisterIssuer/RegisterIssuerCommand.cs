using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;

/// <summary>
/// Registers the issuer of the tenant of the user with its fiscal data and the provider it issues with.
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
/// <param name="Provider">The provider; null for the default of the country.</param>
public record RegisterIssuerCommand(string Country, string TaxIdTypeCode, string TaxId, string LegalName, string PersonTypeCode, string TaxRegimeCode, List<string> TaxResponsibilities, PostalAddress Address, string Email, string Phone, string TimeZoneId, string? Provider) : IRequest
{
    /// <summary>
    /// The fiscal data as the issuer keeps them.
    /// </summary>
    /// <returns>The fiscal data.</returns>
    public IssuerFiscalData ToFiscalData() => new(Country, TaxIdTypeCode, TaxId, LegalName, PersonTypeCode, TaxRegimeCode, TaxResponsibilities ?? [], Address, Email, Phone, TimeZoneId);
}

/// <summary>
/// Validates <see cref="RegisterIssuerCommand"/>.
/// </summary>
public class Validator : AbstractValidator<RegisterIssuerCommand>
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
        RuleFor(x => x.PersonTypeCode).NotEmpty();
        RuleFor(x => x.TaxRegimeCode).NotEmpty();
        RuleFor(x => x.Address).NotNull();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.TimeZoneId).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="RegisterIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="user">The context of the user.</param>
/// <param name="options">The options of the service.</param>
public class RegisterIssuerCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IUserContext user, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<RegisterIssuerCommand>
{
    /// <summary>
    /// Registers the issuer unless the tenant already has one.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(RegisterIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        // The issuer is one per tenant: a second registration is the retry of the first and ends well without repeating.
        if (await issuers.ExistsAsync<IssuerAggregate>(IssuerAggregate.IdFor(user.Tenant), cancellationToken))
        {
            return;
        }

        var provider = IssuerWrites.AllowedProvider(options.Value, factory, request.Country, request.Provider);

        var issuer = IssuerAggregate.Register(user.Tenant, request.ToFiscalData(), provider.Code, user.IdUser);

        await issuers.CreateIfAbsentAsync(issuer, cancellationToken);
    }
}
