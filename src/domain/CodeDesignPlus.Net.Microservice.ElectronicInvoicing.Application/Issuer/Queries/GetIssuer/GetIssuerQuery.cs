using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;

/// <summary>
/// The issuer of a tenant: its enablement, what its provider can do and the documents left in its quota, so the screen
/// that charges does not offer what cannot be invoiced.
/// </summary>
/// <param name="Tenant">The tenant; null for the tenant of the user.</param>
/// <param name="PlatformIssuer">Whether the platform's issuer is asked for; naming a tenant as well is refused (error 218).</param>
public record GetIssuerQuery(Guid? Tenant = null, bool PlatformIssuer = false) : IRequest<IssuerDto?>;

/// <summary>
/// Handles <see cref="GetIssuerQuery"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="quotas">The quotas of documents.</param>
/// <param name="user">The context of the caller.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class GetIssuerQueryHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IQuotaGate quotas, IUserContext user, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<GetIssuerQuery, IssuerDto?>
{
    /// <summary>
    /// Reads the issuer.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issuer, or null when the tenant has none registered.</returns>
    public async Task<IssuerDto?> Handle(GetIssuerQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.Tenant, request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        if (issuer is null)
        {
            return null;
        }

        var quota = await quotas.FindAsync(tenant, cancellationToken);

        return IssuerDto.From(issuer, factory.Find(issuer.Provider)?.Capabilities, quota, options.Value.ProvidersFor(issuer.Country), clock.GetCurrentInstant());
    }
}
