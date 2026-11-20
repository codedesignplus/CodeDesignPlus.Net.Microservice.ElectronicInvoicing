using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;

/// <summary>
/// Counts each document against the quota of its issuer.
/// </summary>
public interface IQuotaGate
{
    /// <summary>
    /// Counts one document. An invoice or a ticket needs room in the period; a note always goes.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="type">The type of the document.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The events of the thresholds crossed and, when this document used the last unit, the one that says the
    /// issuer cannot issue any more, to publish after the document is saved.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>109</c> when an invoice or a ticket finds no room.</exception>
    Task<IReadOnlyList<IDomainEvent>> ReserveAsync(Guid tenant, ElectronicDocumentType type, Instant now, CancellationToken cancellationToken);

    /// <summary>
    /// Gives back the unit of a document that was not created after all, and announces that the issuer can issue again
    /// when that unit was the one missing.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ReleaseAsync(Guid tenant, Instant now, CancellationToken cancellationToken);

    /// <summary>
    /// The quota of a tenant as it stands, refreshing the platform's fixed allowance.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The quota, or null when the tenant never had one.</returns>
    Task<DocumentQuotaAggregate?> FindAsync(Guid tenant, CancellationToken cancellationToken);
}

/// <summary>
/// Counts the documents on the quota of each issuer, with optimistic concurrency: two documents created at once both
/// count.
/// </summary>
/// <remarks>
/// The platform's issuer does not read license attributes: its allowance is the fixed reservation of the configuration
/// for the period of the contract with the provider, applied on every reservation.
/// </remarks>
/// <param name="repository">The repository of the quotas.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="options">The options of the service.</param>
public sealed class QuotaGate(IDocumentQuotaRepository repository, IIssuingAvailabilityAnnouncer availability, IOptions<ElectronicInvoicingOptions> options) : IQuotaGate
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<IDomainEvent>> ReserveAsync(Guid tenant, ElectronicDocumentType type, Instant now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var needsRoom = !DocumentContent.IsNote(type);
        DocumentQuotaAggregate? saved = null;
        var hadRoom = false;

        await repository.UpdateWithRetryAsync(
            async () => await repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken) ?? DocumentQuotaAggregate.CreateEmpty(tenant, settings.SystemUser),
            quota =>
            {
                quota.GetAndClearEvents();

                PlatformAllowance.Apply(quota, tenant, settings, now);

                hadRoom = quota.HasRoomAt(now);

                quota.Reserve(now, needsRoom, settings.Quota.AlertPercentages, settings.SystemUser);

                saved = quota;

                return true;
            },
            cancellationToken);

        List<IDomainEvent> events = [.. saved?.GetAndClearEvents() ?? []];

        // Only the unit that uses up the quota changes the answer to «can it issue?»; a note can be that unit too.
        if (saved is not null && saved.HasRoomAt(now) != hadRoom)
        {
            events.Add(await availability.BuildAsync(tenant, saved, now, cancellationToken));
        }

        return events;
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(Guid tenant, Instant now, CancellationToken cancellationToken)
    {
        DocumentQuotaAggregate? saved = null;
        var hadRoom = false;

        await repository.UpdateWithRetryAsync(
            () => repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken),
            quota =>
            {
                saved = null;
                hadRoom = quota.HasRoomAt(now);

                if (!quota.Release(now, options.Value.SystemUser))
                {
                    return false;
                }

                saved = quota;

                return true;
            },
            cancellationToken);

        // Whoever heard that the quota was used up must hear that a unit came back.
        if (saved is not null && saved.HasRoomAt(now) != hadRoom)
        {
            await availability.PublishAsync(await availability.BuildAsync(tenant, saved, now, cancellationToken), cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<DocumentQuotaAggregate?> FindAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var quota = await repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken);

        return PlatformAllowance.Refresh(quota, tenant, options.Value, SystemClock.Instance.GetCurrentInstant());
    }
}

/// <summary>
/// The fixed allowance of the platform's issuer, which does not come from a license: it is the reservation of the
/// configuration for the period of the contract with the provider.
/// </summary>
public static class PlatformAllowance
{
    /// <summary>
    /// Applies the fixed allowance to the quota of the platform's issuer; the quota of a tenant is left as it is.
    /// </summary>
    /// <param name="quota">The quota.</param>
    /// <param name="tenant">The tenant of the quota.</param>
    /// <param name="settings">The options of the service.</param>
    /// <param name="now">The current instant.</param>
    public static void Apply(DocumentQuotaAggregate quota, Guid tenant, ElectronicInvoicingOptions settings, Instant now)
    {
        if (!PlatformScope.Owns(tenant))
        {
            return;
        }

        quota.ApplyPlatformAllowance(settings.PlatformIssuer.ReservedDocuments, settings.ProviderPlan.From, settings.ProviderPlan.Until, now, settings.SystemUser);
    }

    /// <summary>
    /// The quota as it stands, with the fixed allowance of the platform's issuer even when it was never saved.
    /// </summary>
    /// <param name="quota">The stored quota, or null when the tenant never had one.</param>
    /// <param name="tenant">The tenant of the quota.</param>
    /// <param name="settings">The options of the service.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The quota, or null when a tenant never had one.</returns>
    public static DocumentQuotaAggregate? Refresh(DocumentQuotaAggregate? quota, Guid tenant, ElectronicInvoicingOptions settings, Instant now)
    {
        if (!PlatformScope.Owns(tenant))
        {
            return quota;
        }

        quota ??= DocumentQuotaAggregate.CreateEmpty(tenant, settings.SystemUser);

        Apply(quota, tenant, settings, now);

        return quota;
    }
}
