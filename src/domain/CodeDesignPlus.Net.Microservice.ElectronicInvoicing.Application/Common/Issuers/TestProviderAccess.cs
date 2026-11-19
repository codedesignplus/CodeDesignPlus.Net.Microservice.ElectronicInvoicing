namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;

/// <summary>
/// Whether a tenant may issue with a test provider: an adapter that never reaches a tax authority.
/// </summary>
/// <remarks>
/// The platform's issuer never may: what the company that runs the platform sells is invoiced for real, so its reserved
/// tenant has no permission and cannot be given one.
/// </remarks>
public static class TestProviderAccess
{
    /// <summary>
    /// Whether the permission of a tenant is granted now.
    /// </summary>
    /// <param name="grants">The repository of the permissions.</param>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>false</c> for the platform's issuer, for a tenant that never had it and for one whose permission was revoked.</returns>
    public static async Task<bool> IsGrantedAsync(ITestProviderGrantRepository grants, Guid tenant, CancellationToken cancellationToken)
    {
        if (PlatformScope.Owns(tenant))
        {
            return false;
        }

        var grant = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), cancellationToken);

        return grant is { Granted: true };
    }
}
