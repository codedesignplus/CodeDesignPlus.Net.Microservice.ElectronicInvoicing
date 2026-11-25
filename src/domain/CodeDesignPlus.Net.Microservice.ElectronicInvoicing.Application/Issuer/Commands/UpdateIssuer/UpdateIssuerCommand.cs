using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.UpdateIssuer;

/// <summary>
/// Replaces the fiscal data of the issuer of the tenant of the user.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country.</param>
/// <param name="TaxIdTypeCode">Code of the type of identification in the tax authority's list.</param>
/// <param name="TaxId">Tax id; with its check digit after a hyphen when its type carries one.</param>
/// <param name="LegalName">Legal name.</param>
/// <param name="PersonTypeCode">Code of the type of person.</param>
/// <param name="TaxRegimeCode">Code of the tax regime; null keeps the one the issuer has. Ignored when the tenant has a history of fiscal data managed from outside with a version in force: the issuer takes the one of the history.</param>
/// <param name="TaxResponsibilities">Codes of the fiscal responsibilities. The ones that history manages are replaced by the ones in force, and the code of «no responsibility» stays only when there is no other.</param>
/// <param name="Address">The address.</param>
/// <param name="Email">The email.</param>
/// <param name="Phone">The phone.</param>
/// <param name="TimeZoneId">IANA time zone of the issuer.</param>
public record UpdateIssuerCommand(string Country, string TaxIdTypeCode, string TaxId, string LegalName, string PersonTypeCode, string? TaxRegimeCode, List<string> TaxResponsibilities, PostalAddress Address, string Email, string Phone, string TimeZoneId) : IRequest
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
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="profiles">The histories of fiscal data managed from outside the issuers.</param>
/// <param name="clock">The clock.</param>
/// <param name="user">The context of the user.</param>
/// <param name="options">The options of the service, with the rules of the check digit.</param>
public class UpdateIssuerCommandHandler(IIssuerRepository issuers, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, IElectronicInvoiceProviderFactory factory, IManagedFiscalProfileRepository profiles, IClock clock, IUserContext user, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<UpdateIssuerCommand>
{
    /// <summary>
    /// Replaces the fiscal data, in the provider first when the issuer exists there.
    /// </summary>
    /// <remarks>
    /// An issuer that already exists in a provider that keeps issuers sends them there before keeping them, so its
    /// documents never carry data the provider does not have; if the provider refuses them, nothing changes.
    /// </remarks>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="CodeDesignPlus.Net.Exceptions.CodeDesignPlusException">Thrown with <c>202</c> without issuer, or with the refusal of the provider (for example <c>318</c>).</exception>
    public async Task Handle(UpdateIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);
        var settings = options.Value;

        var current = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(current, Errors.IssuerNotFound);

        var asked = new IssuerFiscalData(request.Country, request.TaxIdTypeCode, request.TaxId, request.LegalName, request.PersonTypeCode, request.TaxRegimeCode ?? current.TaxRegimeCode, request.TaxResponsibilities ?? [], request.Address, request.Email, request.Phone, request.TimeZoneId);
        var data = await IssuerFiscalDataOf.ComposeAsync(asked, tenant, profiles, clock, settings, cancellationToken);

        // Checked on the issuer as it was read, so a refused change never reaches the provider.
        current.UpdateFiscalData(data, user.IdUser, settings.ToCheckDigitRules());

        if (!string.IsNullOrWhiteSpace(current.ProviderIssuerId) && factory.FindProvisioning(current.Provider) is { } provisioning)
        {
            await provisioning.UpdateIssuerAsync(ProviderIssuer.From(current), cancellationToken);
        }

        await IssuerWrites.UpdateAsync(issuers, pubsub, availability, tenant, x => x.UpdateFiscalData(data, user.IdUser, settings.ToCheckDigitRules()), cancellationToken);
    }
}
