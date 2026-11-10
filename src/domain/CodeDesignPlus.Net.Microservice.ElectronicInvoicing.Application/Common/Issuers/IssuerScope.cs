namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;

/// <summary>
/// Which issuer a request works with: the platform's own (<see cref="PlatformScope"/>) or a tenant's.
/// </summary>
/// <remarks>
/// <para>
/// The handlers never fall back to the platform on their own: the entry point decides it. The REST endpoints of the
/// platform send <c>platformIssuer = true</c>; the REST endpoints of a tenant send <c>false</c> and the tenant of the
/// user; the gRPC contract turns a request without a tenant into <c>platformIssuer = true</c>.
/// </para>
/// <para>
/// Asking for the platform and naming a tenant at once is refused (error 218) instead of guessing which one was meant,
/// and the tenant reserved for the platform is refused wherever it comes as a tenant (error 219), so a tenant can never
/// reach the platform's issuer by naming its id.
/// </para>
/// </remarks>
public static class IssuerScope
{
    /// <summary>
    /// The tenant the issuer of the request is kept under.
    /// </summary>
    /// <param name="tenant">The tenant the caller names, if any.</param>
    /// <param name="platformIssuer">Whether the platform's issuer is asked for.</param>
    /// <param name="user">The context of the caller, whose tenant is used when none is named.</param>
    /// <returns>The reserved tenant of the platform, or the tenant named or the user's.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>218</c>, <c>219</c> or <c>111</c> when the scope is ambiguous, reserved or missing.</exception>
    public static Guid TenantOf(Guid? tenant, bool platformIssuer, IUserContext user)
    {
        var named = tenant is { } value && value != Guid.Empty ? value : (Guid?)null;

        ApplicationGuard.IsTrue(platformIssuer && named is not null, Errors.PlatformIssuerWithTenant);

        if (platformIssuer)
        {
            return PlatformScope.Tenant;
        }

        var resolved = named ?? user.Tenant;

        DomainGuard.GuidIsEmpty(resolved, DomainErrors.TenantIsRequired);
        ApplicationGuard.IsTrue(PlatformScope.Owns(resolved), Errors.TenantIsReserved);

        return resolved;
    }

    /// <summary>
    /// The tenant the issuer of a request of the screen is kept under: the platform's, or the one of the user.
    /// </summary>
    /// <param name="platformIssuer">Whether the platform's issuer is asked for.</param>
    /// <param name="user">The context of the user.</param>
    /// <returns>The reserved tenant of the platform, or the user's.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>219</c> or <c>111</c> when the user's tenant is reserved or missing.</exception>
    public static Guid TenantOf(bool platformIssuer, IUserContext user) => TenantOf(null, platformIssuer, user);
}
