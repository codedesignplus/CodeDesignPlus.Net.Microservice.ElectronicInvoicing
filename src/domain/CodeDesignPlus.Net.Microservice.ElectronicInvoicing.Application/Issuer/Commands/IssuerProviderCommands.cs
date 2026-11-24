using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

/// <summary>
/// Creates the issuer of the tenant of the user in its provider, when the provider creates issuers through its API.
/// </summary>
/// <param name="FirstName">First names of the person the provider registers as its user.</param>
/// <param name="LastName">Last names of that person.</param>
/// <param name="Email">Email of that person.</param>
/// <param name="Phone">Phone of that person.</param>
public record ProvisionIssuerCommand(string FirstName, string LastName, string Email, string Phone) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="ProvisionIssuerCommand"/>.
/// </summary>
public class ProvisionIssuerValidator : AbstractValidator<ProvisionIssuerCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public ProvisionIssuerValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="ProvisionIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="user">The context of the user.</param>
public class ProvisionIssuerCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, IUserContext user) : IRequestHandler<ProvisionIssuerCommand>
{
    /// <summary>
    /// Creates the issuer in the provider and keeps its id there.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(ProvisionIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        // Created already: the retry ends well without creating a second one in the provider.
        if (!string.IsNullOrWhiteSpace(issuer.ProviderIssuerId))
        {
            return;
        }

        var provisioning = factory.FindProvisioning(issuer.Provider);

        ApplicationGuard.IsNull(provisioning, Errors.ProviderCannotProvisionIssuers);
        DomainGuard.IsNull(issuer.Mandate, DomainErrors.MandateNotAccepted);

        var provisioned = await provisioning.ProvisionAsync(ProviderIssuer.From(issuer), new IssuerContact(request.FirstName, request.LastName, request.Email, request.Phone), cancellationToken);

        await IssuerWrites.UpdateAsync(issuers, pubsub, availability, tenant, x => x.MarkProvisioned(provisioned.ProviderIssuerId, user.IdUser), cancellationToken);
    }
}
