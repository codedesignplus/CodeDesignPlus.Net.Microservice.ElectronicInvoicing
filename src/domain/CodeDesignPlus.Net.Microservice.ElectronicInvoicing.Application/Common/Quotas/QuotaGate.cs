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
    /// <returns>The events of the thresholds crossed, to publish after the document is saved.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>109</c> when an invoice or a ticket finds no room.</exception>
    Task<IReadOnlyList<IDomainEvent>> ReserveAsync(Guid tenant, ElectronicDocumentType type, Instant now, CancellationToken cancellationToken);

    /// <summary>
    /// Gives back the unit of a document that was not created after all.
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
/// <param name="options">The options of the service.</param>
public sealed class QuotaGate(IDocumentQuotaRepository repository, IOptions<ElectronicInvoicingOptions> options) : IQuotaGate
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<IDomainEvent>> ReserveAsync(Guid tenant, ElectronicDocumentType type, Instant now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var needsRoom = !DocumentContent.IsNote(type);
        DocumentQuotaAggregate? saved = null;

        await repository.UpdateWithRetryAsync(
            async () => await repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken) ?? DocumentQuotaAggregate.CreateEmpty(tenant, settings.SystemUser),
            quota =>
            {
                quota.GetAndClearEvents();

                ApplyPlatformAllowance(quota, tenant, now);

                quota.Reserve(now, needsRoom, settings.Quota.AlertPercentages, settings.SystemUser);

                saved = quota;

                return true;
            },
            cancellationToken);

        return saved?.GetAndClearEvents() ?? [];
    }

    /// <inheritdoc/>
    public Task ReleaseAsync(Guid tenant, Instant now, CancellationToken cancellationToken)
    {
        return repository.UpdateWithRetryAsync(
            () => repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken),
            quota => quota.Release(now, options.Value.SystemUser),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DocumentQuotaAggregate?> FindAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var quota = await repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(tenant), cancellationToken);

        if (tenant == options.Value.PlatformTenant)
        {
            quota ??= DocumentQuotaAggregate.CreateEmpty(tenant, options.Value.SystemUser);

            ApplyPlatformAllowance(quota, tenant, SystemClock.Instance.GetCurrentInstant());
        }

        return quota;
    }

    private void ApplyPlatformAllowance(DocumentQuotaAggregate quota, Guid tenant, Instant now)
    {
        var settings = options.Value;

        if (tenant != settings.PlatformTenant)
        {
            return;
        }

        quota.ApplyPlatformAllowance(settings.PlatformIssuer.ReservedDocuments, settings.ProviderPlan.From, settings.ProviderPlan.Until, now, settings.SystemUser);
    }
}
