using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
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
/// configuration, so its license is ignored. Every allowance that changes the quota (a bigger or a smaller one, another
/// end, a period that starts) is announced with how the issuer stands (<c>IssuingAvailabilityChangedDomainEvent</c>):
/// an allowance below what is already used leaves no room, and whoever listens would never hear it otherwise.
/// </remarks>
/// <param name="repository">The repository of the quotas.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class ApplyLicenseQuotaCommandHandler(IDocumentQuotaRepository repository, IIssuingAvailabilityAnnouncer availability, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<ApplyLicenseQuotaCommand, bool>
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

        // The platform's issuer has no license; a license event naming its reserved tenant is not a tenant's.
        if (PlatformScope.Owns(request.Tenant))
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

        List<IDomainEvent> events = [.. saved?.GetAndClearEvents() ?? []];

        if (saved is not null)
        {
            events.Add(await availability.BuildAsync(request.Tenant, saved, now, cancellationToken));
        }

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
/// The round of the watch of the provider's plan: counts the documents of the current month, with its overage and its
/// estimated cost, and announces the thresholds crossed and the end of the contract.
/// </summary>
public record WatchProviderPlanCommand : IRequest<long>;

/// <summary>
/// Handles <see cref="WatchProviderPlanCommand"/>.
/// </summary>
/// <remarks>
/// The plan includes documents per calendar month that do not carry over, so each month has its own usage. The round
/// also counts again the previous month while its last count is older than its end: the documents validated in its last
/// hours are not lost from its total. Going over the included documents never blocks: it is only announced.
/// </remarks>
/// <param name="meter">Measures a month of the plan.</param>
/// <param name="usages">The repository of the usage of the plans.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class WatchProviderPlanCommandHandler(IProviderPlanMeter meter, IProviderPlanUsageRepository usages, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<WatchProviderPlanCommand, long>
{
    /// <summary>
    /// Runs the watch.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The documents of the current month; zero when the contract does not cover it.</returns>
    public async Task<long> Handle(WatchProviderPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = options.Value.ProviderPlan;
        var now = clock.GetCurrentInstant();
        var month = plan.MonthOf(now);

        await CloseAsync(month.PlusMonths(-1), now, cancellationToken);

        var current = await meter.MeasureAsync(month, cancellationToken);

        if (current is null)
        {
            return 0;
        }

        await RecordAsync(current, now, cancellationToken);

        return current.Used;
    }

    private async Task CloseAsync(LocalDate month, Instant now, CancellationToken cancellationToken)
    {
        var plan = options.Value.ProviderPlan;

        if (plan.PeriodOf(month) is not { } period)
        {
            return;
        }

        var saved = await usages.FindAsync<ProviderPlanUsageAggregate>(ProviderPlanUsageAggregate.IdFor(plan.Provider, period.Start), cancellationToken);

        if (saved is null || saved.CountedAt >= saved.PeriodEnd)
        {
            return;
        }

        var measured = await meter.MeasureAsync(month, cancellationToken);

        if (measured is not null)
        {
            await RecordAsync(measured, now, cancellationToken);
        }
    }

    private async Task RecordAsync(ProviderPlanMonth measured, Instant now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var plan = settings.ProviderPlan;
        ProviderPlanUsageAggregate? saved = null;

        await usages.UpdateWithRetryAsync(
            async () => await usages.FindAsync<ProviderPlanUsageAggregate>(ProviderPlanUsageAggregate.IdFor(measured.Provider, measured.PeriodStart), cancellationToken) ?? ProviderPlanUsageAggregate.Start(measured.Provider, measured.Month, measured.PeriodStart, measured.PeriodEnd, measured.Currency, settings.SystemUser),
            usage =>
            {
                usage.GetAndClearEvents();
                usage.Record(measured.Used, measured.IncludedDocuments, plan.Tiers(), plan.Until, plan.AlertPercentages, Duration.FromTimeSpan(plan.ExpiryNotice), now, settings.SystemUser);

                saved = usage;

                return true;
            },
            cancellationToken);

        var events = saved?.GetAndClearEvents() ?? [];

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }
    }
}

/// <summary>
/// How a calendar month of the provider's plan stands, for whoever runs the platform.
/// </summary>
/// <param name="Month">Any day of the month; the current month when it is null.</param>
public record GetProviderPlanMonthQuery(LocalDate? Month) : IRequest<ProviderPlanMonth?>;

/// <summary>
/// Handles <see cref="GetProviderPlanMonthQuery"/>: measures the month now, without waiting for the watch job.
/// </summary>
/// <param name="meter">Measures a month of the plan.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class GetProviderPlanMonthQueryHandler(IProviderPlanMeter meter, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<GetProviderPlanMonthQuery, ProviderPlanMonth?>
{
    /// <summary>
    /// Measures the month.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How the month stands; null when the contract does not cover it.</returns>
    public Task<ProviderPlanMonth?> Handle(GetProviderPlanMonthQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var month = request.Month ?? options.Value.ProviderPlan.MonthOf(clock.GetCurrentInstant());

        return meter.MeasureAsync(month, cancellationToken);
    }
}
