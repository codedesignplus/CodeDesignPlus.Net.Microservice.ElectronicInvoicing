namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// The scope of the platform's own issuer: the company that runs the platform and invoices what it sells. It is not a
/// tenant of anyone, so its issuer, numbering ranges, documents and quota are kept under a tenant id reserved by this
/// service.
/// </summary>
/// <remarks>
/// <para>
/// Every aggregate of the service is filtered by tenant, and the SDK refuses <see cref="Guid.Empty"/> as the tenant of an
/// aggregate (the Mongo filters and the purge by tenant throw), so the platform needs a non-empty id of its own. It is a
/// constant and not configuration: a configured id could point at a real tenant, and changing it would orphan the
/// platform's issuer and its documents.
/// </para>
/// <para>
/// It cannot collide with a real tenant: its version nibble (first digit of the third group) and its variant nibble
/// (first digit of the fourth group) are zero, a shape no RFC 9562 generator (v4, v7) produces, and the application
/// refuses it wherever a tenant comes from outside (the gRPC request, the user's context, the license and purge events).
/// </para>
/// </remarks>
public static class PlatformScope
{
    /// <summary>
    /// The tenant id the platform's issuer and everything it owns are kept under.
    /// </summary>
    public static readonly Guid Tenant = Guid.Parse("00000000-e1ec-0000-0000-000000000001");

    /// <summary>
    /// Whether a tenant id is the one reserved for the platform's issuer.
    /// </summary>
    /// <param name="tenant">The tenant id.</param>
    /// <returns><c>true</c> when it is the platform's.</returns>
    public static bool Owns(Guid tenant) => tenant == Tenant;
}
