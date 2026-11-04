namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// The totals of an electronic document in minor units of its currency.
/// </summary>
/// <param name="Currency">ISO 4217 code of the currency.</param>
/// <param name="CurrencyDecimalDigits">Decimal digits of the currency, resolved when the document was created; the adapters convert the minor units with them.</param>
/// <param name="GrossAmount">Sum of quantity times unit price of every line, before discounts.</param>
/// <param name="DiscountAmount">Sum of the discounts of the lines.</param>
/// <param name="TaxAmount">Sum of the taxes of the lines.</param>
/// <param name="WithholdingAmount">Sum of the withholdings of the lines.</param>
/// <param name="PrepaymentAmount">Sum of the prepayments applied to the document.</param>
/// <param name="PayableAmount">What the buyer pays: gross minus discounts plus taxes minus prepayments.</param>
public sealed record DocumentTotals(string Currency, short CurrencyDecimalDigits, long GrossAmount, long DiscountAmount, long TaxAmount, long WithholdingAmount, long PrepaymentAmount, long PayableAmount)
{
    /// <summary>
    /// The totals in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(Currency, CurrencyDecimalDigits, GrossAmount, DiscountAmount, TaxAmount, WithholdingAmount, PrepaymentAmount, PayableAmount);
}
