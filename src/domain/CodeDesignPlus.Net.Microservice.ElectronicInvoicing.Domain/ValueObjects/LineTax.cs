namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// A tax or a withholding of a line, as the caller computed it.
/// </summary>
/// <param name="Code">Code of the tax in the tax authority's list (for example <c>01</c> for VAT).</param>
/// <param name="Name">Name of the tax as it is printed.</param>
/// <param name="RateBasisPoints">Rate in basis points (1900 = 19 %).</param>
/// <param name="BaseAmount">Taxable base, in minor units.</param>
/// <param name="Amount">Amount of the tax, in minor units.</param>
public sealed record LineTax(string Code, string Name, int RateBasisPoints, long BaseAmount, long Amount)
{
    /// <summary>
    /// The tax in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(Code, Name, RateBasisPoints, BaseAmount, Amount);
}
