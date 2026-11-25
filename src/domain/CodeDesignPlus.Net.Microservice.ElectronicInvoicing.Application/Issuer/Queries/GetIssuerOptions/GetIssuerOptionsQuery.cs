using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuerOptions;

/// <summary>
/// What the form of an issuer offers: the lists of the tax authority of its country, the version of the mandate in force
/// and the providers the issuer of the scope may choose, so the screen offers only what this service accepts.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country; null for the only one of the routing.</param>
/// <param name="PlatformIssuer">Whether it is asked for the platform's issuer instead of the one of the tenant of the user.</param>
public record GetIssuerOptionsQuery(string? Country = null, bool PlatformIssuer = false) : IRequest<IssuerOptionsDto>;

/// <summary>
/// Handles <see cref="GetIssuerOptionsQuery"/>.
/// </summary>
/// <param name="grants">The permissions to issue with a test provider.</param>
/// <param name="user">The context of the caller.</param>
/// <param name="options">The options of the service.</param>
/// <param name="profiles">The histories of fiscal data managed from outside the issuers.</param>
/// <param name="clock">The clock.</param>
public class GetIssuerOptionsQueryHandler(ITestProviderGrantRepository grants, IUserContext user, IOptions<ElectronicInvoicingOptions> options, IManagedFiscalProfileRepository profiles, IClock clock) : IRequestHandler<GetIssuerOptionsQuery, IssuerOptionsDto>
{
    /// <summary>
    /// Reads the options of the form from the configuration.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lists, the version of the mandate and the providers.</returns>
    /// <exception cref="CodeDesignPlus.Net.Exceptions.CodeDesignPlusException">When no country is named and the routing has several (error 226), or the country has no electronic invoicing (error 227).</exception>
    public async Task<IssuerOptionsDto> Handle(GetIssuerOptionsQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var settings = options.Value;
        var country = request.Country;

        if (string.IsNullOrWhiteSpace(country))
        {
            ApplicationGuard.IsTrue(settings.Routing.Count != 1, Errors.IssuerCountryIsRequired);

            country = settings.Routing.Keys.Single();
        }

        var catalogs = settings.IssuerOptions.For(country);
        var providers = settings.ProvidersFor(country);

        ApplicationGuard.IsTrue(catalogs is null || providers.Count == 0, Errors.CountryWithoutElectronicInvoicing);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);
        var granted = settings.TestProviders.Count > 0 && await TestProviderAccess.IsGrantedAsync(grants, tenant, cancellationToken);
        var available = settings.ProvidersFor(country, granted);

        var dto = IssuerOptionsDto.From(catalogs!.Value.Country, catalogs.Value.Catalogs, settings.IssuerOptions.MandateVersion, available, [.. available.Where(settings.IsTestProvider)]);

        var profile = PlatformScope.Owns(tenant) ? null : await profiles.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(tenant), cancellationToken);

        if (profile is not null)
        {
            dto.FiscalProfileResponsibilities = [.. profile.ManagedResponsibilities];

            if (profile.InForceAt(clock.GetCurrentInstant()) is { } entry)
            {
                dto.FiscalProfile = new FiscalProfileSideDto(entry.TaxRegimeCode, [.. entry.TaxResponsibilities]);
            }
        }

        return dto;
    }
}
