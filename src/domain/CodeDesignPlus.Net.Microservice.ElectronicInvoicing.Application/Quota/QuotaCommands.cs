using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

/// <summary>
/// Applies to the quota of a tenant the attributes its license has now.
/// </summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Attributes">The effective attributes of its license, as ms-tenants publishes them.</param>
/// <param name="LicenseEnd">When the license ends: the period of the quota ends there.</param>
/// <param name="PublishedAt">When the attributes were published, so an older publication arriving late does not undo a newer one.</param>
public record ApplyLicenseQuotaCommand(Guid Tenant, IReadOnlyDictionary<string, string> Attributes, Instant LicenseEnd, Instant PublishedAt) : IRequest<bool>;

/// <summary>
/// Handles <see cref="ApplyLicenseQuotaCommand"/>.
/// </summary>
/// <remarks>
/// The quota is the sum of the integer values of the keys configured in <c>ElectronicInvoicing:Quota:AttributeKeys</c>;
/// a missing key or a value that is not an integer counts as zero (the most restrictive). This service does not know
/// what a package is. The platform's issuer does not read attributes: its quota is the fixed reservation of the
/// configuration, so its license is ignored.
/// </remarks>
/// <param name="repository">The repository of the quotas.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class ApplyLicenseQuotaCommandHandler(IDocumentQuotaRepository repository, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<ApplyLicenseQuotaCommand, bool>
{
    /// <summary>
    /// Applies the quota.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the quota changed.</returns>
    public async Task<bool> Handle(ApplyLicenseQuotaCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var settings = options.Value;

        if (request.Tenant == settings.PlatformTenant)
        {
            return false;
        }

        var limit = LimitOf(request.Attributes, settings.Quota.AttributeKeys);
        var now = clock.GetCurrentInstant();
        DocumentQuotaAggregate? saved = null;

        var written = await repository.UpdateWithRetryAsync(
            async () => await repository.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(request.Tenant), cancellationToken) ?? DocumentQuotaAggregate.CreateEmpty(request.Tenant, settings.SystemUser),
            quota =>
            {
                saved = null;
                quota.GetAndClearEvents();

                // A new quota is saved even when its first allowance changes nothing, so the period is known.
                var changed = quota.ApplyAllowance(limit, request.LicenseEnd, request.PublishedAt, now, settings.SystemUser) || quota.Version == 0;

                saved = changed ? quota : null;

                return changed;
            },
            cancellationToken);

        var events = saved?.GetAndClearEvents() ?? [];

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }

        return written;
    }

    /// <summary>
    /// The quota the attributes allow: the sum of the integer values of the configured keys.
    /// </summary>
    /// <param name="attributes">The attributes of the license.</param>
    /// <param name="keys">The configured keys.</param>
    /// <returns>The quota; zero when no key is there.</returns>
    public static long LimitOf(IReadOnlyDictionary<string, string>? attributes, IEnumerable<string> keys)
    {
        if (attributes is null)
        {
            return 0;
        }

        long limit = 0;

        foreach (var key in keys)
        {
            if (attributes.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
            {
                limit += number;
            }
        }

        return limit;
    }
}

/// <summary>
/// The round of the watch of the provider's plan: counts the documents sent in its period and announces the thresholds
/// crossed and the end of the contract.
/// </summary>
public record WatchProviderPlanCommand : IRequest<long>;

/// <summary>
/// Handles <see cref="WatchProviderPlanCommand"/>.
/// </summary>
/// <remarks>
/// The count is every document the provider received for the first time in the period of the plan, of every issuer
/// that issues with the provider of the plan: the plan is shared among them, and the documents of an issuer with another
/// provider do not spend it. An issuer that changed provider counts all its documents for the provider it has now. It
/// is a count and not a sum kept on each send, so nobody shares a document on the path of a send.
/// </remarks>
/// <param name="documents">The repository of the documents.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="usages">The repository of the usage of the plans.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class WatchProviderPlanCommandHandler(IElectronicDocumentRepository documents, IIssuerRepository issuers, IProviderPlanUsageRepository usages, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<WatchProviderPlanCommand, long>
{
    /// <summary>
    /// Runs the watch.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The documents sent in the period of the plan.</returns>
    public async Task<long> Handle(WatchProviderPlanCommand request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var plan = settings.ProviderPlan;
        var now = clock.GetCurrentInstant();

        // Only the documents of the issuers of the plan's provider spend it: with several providers, each has its plan.
        var issuersOfThePlan = await issuers.FindIdsByProviderAsync(plan.Provider, cancellationToken);
        var used = await documents.CountFirstSentBetweenAsync(plan.From, plan.Until, issuersOfThePlan, cancellationToken);
        ProviderPlanUsageAggregate? saved = null;

        await usages.UpdateWithRetryAsync(
            async () => await usages.FindAsync<ProviderPlanUsageAggregate>(ProviderPlanUsageAggregate.IdFor(plan.Provider, plan.From), cancellationToken) ?? ProviderPlanUsageAggregate.Start(settings.PlatformTenant, plan.Provider, plan.Documents, plan.From, plan.Until, settings.SystemUser),
            usage =>
            {
                usage.GetAndClearEvents();
                usage.Record(used, plan.Documents, plan.Until, plan.AlertPercentages, Duration.FromTimeSpan(plan.ExpiryNotice), now, settings.SystemUser);

                saved = usage;

                return true;
            },
            cancellationToken);

        var events = saved?.GetAndClearEvents() ?? [];

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }

        return used;
    }
}
