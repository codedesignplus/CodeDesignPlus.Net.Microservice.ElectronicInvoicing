using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;

/// <summary>
/// Says how an issuer stands (whether it can issue a new document, and why not) to whoever listens, each time that
/// answer changes.
/// </summary>
public interface IIssuingAvailabilityAnnouncer
{
    /// <summary>
    /// The event that says how the issuer of a tenant stands, with a quota already read.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="quota">Its quota as it was just saved, or null when it never had one.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The event, not yet published.</returns>
    Task<IDomainEvent> BuildAsync(Guid tenant, DocumentQuotaAggregate? quota, Instant now, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes an event built with <see cref="BuildAsync"/>.
    /// </summary>
    /// <param name="availability">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(IDomainEvent availability, CancellationToken cancellationToken);

    /// <summary>
    /// Reads how the issuer of a tenant stands now and publishes it.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AnnounceAsync(Guid tenant, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes how the issuer of a tenant stands when the permission of its test provider changed: only when it has an
    /// issuer that issues with a test provider that needs the permission, the only one the change matters to.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AnnounceForTestProviderAsync(Guid tenant, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the state from the issuer, its quota and the permission of its provider, with the same checks a new document
/// goes through, in the same order: the issuer exists (<c>202</c>), has a mandate (<c>108</c>), can issue (<c>101</c>),
/// its test provider is still allowed (<c>225</c>) and its quota has room (<c>109</c>).
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="quotas">The repository of the quotas.</param>
/// <param name="grants">The permissions to issue with a test provider.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public sealed class IssuingAvailabilityAnnouncer(IIssuerRepository issuers, IDocumentQuotaRepository quotas, ITestProviderGrantRepository grants, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IIssuingAvailabilityAnnouncer
{
    /// <inheritdoc/>
    public async Task<IDomainEvent> BuildAsync(Guid tenant, DocumentQuotaAggregate? quota, Instant now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);
        var permitted = issuer is null || !settings.NeedsTestProviderGrant(issuer.Country, issuer.Provider) || await TestProviderAccess.IsGrantedAsync(grants, tenant, cancellationToken);

        return Of(tenant, issuer, permitted, quota?.StandingAt(now) ?? QuotaStanding.None);
    }

    /// <inheritdoc/>
    public Task PublishAsync(IDomainEvent availability, CancellationToken cancellationToken) => pubsub.PublishAsync([availability], cancellationToken);

    /// <inheritdoc/>
    public async Task AnnounceAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var stored = await quotas.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken);
        var quota = PlatformAllowance.Refresh(stored, tenant, options.Value, now);

        await PublishAsync(await BuildAsync(tenant, quota, now, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AnnounceForTestProviderAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        if (issuer is null || !options.Value.NeedsTestProviderGrant(issuer.Country, issuer.Provider))
        {
            return;
        }

        await AnnounceAsync(tenant, cancellationToken);
    }

    /// <summary>
    /// The state of an issuer as the event says it.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="issuer">The issuer, or null when the tenant has none.</param>
    /// <param name="permitted">Whether its provider is allowed to it: false only for a test provider whose permission is not granted.</param>
    /// <param name="quota">How its quota stands.</param>
    /// <returns>The event.</returns>
    public static IssuingAvailabilityChangedDomainEvent Of(Guid tenant, IssuerAggregate? issuer, bool permitted, QuotaStanding quota)
    {
        ArgumentNullException.ThrowIfNull(quota);

        var issuerCanIssue = issuer is { CanIssue: true } && permitted;
        var hasRoom = quota.Remaining > 0;

        return new IssuingAvailabilityChangedDomainEvent(
            IssuerAggregate.IdFor(tenant),
            tenant,
            issuerCanIssue && hasRoom,
            ReasonOf(issuer, permitted, hasRoom),
            issuer is not null,
            issuerCanIssue,
            issuer?.Status.ToString(),
            hasRoom,
            quota.Limit,
            quota.Used,
            quota.Remaining,
            quota.PeriodStart,
            quota.PeriodEnd,
            quota.RenewedUntil);
    }

    private static string? ReasonOf(IssuerAggregate? issuer, bool permitted, bool hasRoom)
    {
        if (issuer is null)
        {
            return Errors.IssuerNotFound.GetCode();
        }

        if (issuer.Mandate is null)
        {
            return DomainErrors.MandateNotAccepted.GetCode();
        }

        if (!issuer.CanIssue)
        {
            return DomainErrors.IssuerNotEnabled.GetCode();
        }

        if (!permitted)
        {
            return Errors.TestProviderNotGranted.GetCode();
        }

        return hasRoom ? null : DomainErrors.QuotaExhausted.GetCode();
    }
}
