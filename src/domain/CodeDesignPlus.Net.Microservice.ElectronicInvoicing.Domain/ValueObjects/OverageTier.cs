namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// A tier of the price of the documents a month goes over the ones the provider's plan includes.
/// </summary>
/// <param name="UpTo">The last document of the month, counted from the first one, this tier prices; null for the last tier, which has no end.</param>
/// <param name="UnitPrice">The price of each document of the tier, in minor units of the currency of the plan.</param>
public sealed record OverageTier(long? UpTo, long UnitPrice)
{
    /// <summary>
    /// What the documents over the included ones cost, tier by tier: each document is priced by the tier its position
    /// in the month falls in, as the provider bills them (the tiers add up, they do not reprice the whole month).
    /// </summary>
    /// <param name="used">The documents of the month.</param>
    /// <param name="included">The documents the plan includes in the month.</param>
    /// <param name="tiers">The tiers, in order: each one starts where the previous one ends, and the first one after the included documents.</param>
    /// <returns>The cost in minor units of the currency of the plan; zero when the month does not go over.</returns>
    public static long CostOf(long used, long included, IEnumerable<OverageTier> tiers)
    {
        var cost = 0L;
        var start = included;

        foreach (var tier in tiers ?? [])
        {
            var end = Math.Min(used, tier.UpTo ?? long.MaxValue);

            if (end > start)
            {
                cost += (end - start) * tier.UnitPrice;
            }

            start = Math.Max(start, tier.UpTo ?? long.MaxValue);
        }

        return cost;
    }
}
