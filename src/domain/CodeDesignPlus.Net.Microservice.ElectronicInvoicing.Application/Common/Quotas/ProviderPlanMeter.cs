using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;

/// <summary>
/// How a calendar month of the provider's plan stands: the documents the provider bills in it, the ones it includes, the
/// overage and what the overage is estimated to cost.
/// </summary>
/// <param name="Provider">The provider of the plan.</param>
/// <param name="Month">The first day of the month, in the time zone of the plan.</param>
/// <param name="PeriodStart">When the counted period starts.</param>
/// <param name="PeriodEnd">When the counted period ends, exclusive.</param>
/// <param name="IncludedDocuments">Documents the plan includes in the month.</param>
/// <param name="Used">Documents of the month.</param>
/// <param name="Overage">Documents of the month over the included ones.</param>
/// <param name="EstimatedOverageCost">What the overage is estimated to cost, in minor units of <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO 4217 code of the currency of the plan.</param>
public sealed record ProviderPlanMonth(string Provider, LocalDate Month, Instant PeriodStart, Instant PeriodEnd, long IncludedDocuments, long Used, long Overage, long EstimatedOverageCost, string Currency);

/// <summary>
/// Measures a calendar month of the provider's plan.
/// </summary>
public interface IProviderPlanMeter
{
    /// <summary>
    /// Measures a month.
    /// </summary>
    /// <param name="month">Any day of the month.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How the month stands; null when the contract does not cover it.</returns>
    Task<ProviderPlanMonth?> MeasureAsync(LocalDate month, CancellationToken cancellationToken);
}

/// <summary>
/// Counts what the provider bills: the documents the tax authority validated in the month, of the issuers of the
/// provider of the plan that issue in production. The documents of the tests, the rejected ones and those of an issuer
/// with another provider do not spend the plan.
/// </summary>
/// <remarks>
/// It is a count and not a sum kept on each send, so nobody shares a document on the path of a send. An issuer is taken
/// by the environment it has now: an issuer that went to production in the month counts the documents it had validated
/// in the month before, so the cost is an estimate and the provider's invoice is what rules.
/// </remarks>
/// <param name="documents">The repository of the documents.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="options">The options of the service.</param>
public sealed class ProviderPlanMeter(IElectronicDocumentRepository documents, IIssuerRepository issuers, IOptions<ElectronicInvoicingOptions> options) : IProviderPlanMeter
{
    /// <inheritdoc/>
    public async Task<ProviderPlanMonth?> MeasureAsync(LocalDate month, CancellationToken cancellationToken)
    {
        var plan = options.Value.ProviderPlan;
        var first = new LocalDate(month.Year, month.Month, 1);

        if (plan.PeriodOf(first) is not { } period)
        {
            return null;
        }

        var issuersOfThePlan = await issuers.FindIdsByProviderAsync(plan.Provider, IssuerEnvironment.Production, cancellationToken);
        var used = issuersOfThePlan.Count == 0 ? 0 : await documents.CountValidatedBetweenAsync(period.Start, period.End, issuersOfThePlan, cancellationToken);

        return new ProviderPlanMonth(plan.Provider, first, period.Start, period.End, plan.DocumentsPerMonth, used, Math.Max(0, used - plan.DocumentsPerMonth), OverageTier.CostOf(used, plan.DocumentsPerMonth, plan.Tiers()), plan.Currency);
    }
}
