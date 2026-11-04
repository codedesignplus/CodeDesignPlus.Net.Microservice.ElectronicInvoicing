namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// A line of an electronic document: what is sold, how much and with which taxes. The caller decides the concept and
/// the taxes; this service only checks that they add up.
/// </summary>
/// <param name="Code">The caller's code of the product or service.</param>
/// <param name="Description">What is sold, as it is printed.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="UnitCode">Code of the unit of measure in the tax authority's list (for example <c>94</c>).</param>
/// <param name="UnitPrice">Price of one unit before taxes, in minor units.</param>
/// <param name="DiscountAmount">Discount of the whole line, in minor units.</param>
/// <param name="Amount">Net amount of the line: quantity times unit price minus the discount, in minor units.</param>
/// <param name="Taxes">The taxes of the line.</param>
/// <param name="Withholdings">The withholdings of the line.</param>
public sealed record DocumentLine(string Code, string Description, decimal Quantity, string UnitCode, long UnitPrice, long DiscountAmount, long Amount, List<LineTax> Taxes, List<LineTax> Withholdings)
{
    /// <summary>
    /// Quantity times unit price, in minor units, rounded half away from zero.
    /// </summary>
    /// <remarks>
    /// A fractional quantity can give a fraction of a minor unit; the check of the totals uses this same value.
    /// </remarks>
    public long GrossAmount => (long)Math.Round(Quantity * UnitPrice, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The line in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(Code, Description, Quantity, UnitCode, UnitPrice, DiscountAmount, Amount, Canonicalizer.List((Taxes ?? []).Select(x => x.Canonical())), Canonicalizer.List((Withholdings ?? []).Select(x => x.Canonical())));
}
