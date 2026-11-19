namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// The permission of a tenant to issue with a test provider: an adapter that never reaches a tax authority.
/// </summary>
/// <remarks>
/// <para>
/// Whoever runs the platform grants it to the tenants it tests with, so their issuers can choose a provider that the
/// routing of their country does not offer to everyone. It lives apart from the issuer because it is decided before the
/// issuer is registered, and by someone else. One per tenant, with an id derived from it.
/// </para>
/// <para>
/// Revoking it keeps the document, with who revoked it and when: the issuer of the tenant stops issuing with the test
/// provider, and what it already issued stays as it is.
/// </para>
/// <para>
/// Two operators may grant and revoke at once, hence <see cref="IVersionedAggregate"/>.
/// </para>
/// </remarks>
public class TestProviderGrantAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <summary>
    /// Namespace of the ids derived from the tenant. Changing it would orphan every stored permission.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("8d2e5f17-a4c3-4b96-9f08-2c7e1b6d3a54");

    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// The tenant the permission is of.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// Whether the tenant may issue with a test provider now.
    /// </summary>
    public bool Granted { get; private set; }

    /// <summary>
    /// Who granted it last.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    /// <summary>
    /// When it was granted last.
    /// </summary>
    public Instant GrantedAt { get; private set; }

    /// <summary>
    /// Who revoked it, while it is revoked.
    /// </summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// When it was revoked, while it is revoked.
    /// </summary>
    public Instant? RevokedAt { get; private set; }

    /// <summary>
    /// The id of the permission of a tenant: always the same for the same tenant.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid tenant) => DeterministicGuid.Create(IdNamespace, tenant.ToString());

    /// <summary>
    /// Grants the permission to a tenant that never had it.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="grantedBy">Who grants it.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The permission granted, not yet saved (version 0).</returns>
    public static TestProviderGrantAggregate Grant(Guid tenant, Guid grantedBy, Instant now)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.GuidIsEmpty(grantedBy, Errors.CreatedByIsRequired);
        DomainGuard.IsTrue(PlatformScope.Owns(tenant), Errors.TestProviderNotForThePlatform);

        var grant = new TestProviderGrantAggregate(IdFor(tenant))
        {
            Tenant = tenant,
            Granted = true,
            GrantedBy = grantedBy,
            GrantedAt = now,
            IsActive = true,
            CreatedBy = grantedBy,
            CreatedAt = now
        };

        grant.AddEvent(new TestProviderGrantedDomainEvent(grant.Id, tenant, grantedBy));

        return grant;
    }

    /// <summary>
    /// Grants again a permission that was revoked.
    /// </summary>
    /// <param name="grantedBy">Who grants it.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>False when it was already granted, so nothing is written.</returns>
    public bool GrantAgain(Guid grantedBy, Instant now)
    {
        DomainGuard.GuidIsEmpty(grantedBy, Errors.CreatedByIsRequired);

        if (Granted)
        {
            return false;
        }

        Granted = true;
        GrantedBy = grantedBy;
        GrantedAt = now;
        RevokedBy = null;
        RevokedAt = null;
        UpdatedBy = grantedBy;
        UpdatedAt = now;

        AddEvent(new TestProviderGrantedDomainEvent(Id, Tenant, grantedBy));

        return true;
    }

    /// <summary>
    /// Revokes the permission: the issuer of the tenant stops issuing with a test provider.
    /// </summary>
    /// <param name="revokedBy">Who revokes it.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>False when it was already revoked, so nothing is written.</returns>
    public bool Revoke(Guid revokedBy, Instant now)
    {
        DomainGuard.GuidIsEmpty(revokedBy, Errors.CreatedByIsRequired);

        if (!Granted)
        {
            return false;
        }

        Granted = false;
        RevokedBy = revokedBy;
        RevokedAt = now;
        UpdatedBy = revokedBy;
        UpdatedAt = now;

        AddEvent(new TestProviderRevokedDomainEvent(Id, Tenant, revokedBy));

        return true;
    }
}
