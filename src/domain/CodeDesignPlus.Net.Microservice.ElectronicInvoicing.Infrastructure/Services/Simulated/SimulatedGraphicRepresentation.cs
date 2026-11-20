using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// The graphic representation of a simulated document: a PDF anyone can open and print, with what the document says
/// (issuer, type, number, dates, buyer, cash register, lines, taxes, totals and the simulated CUFE) and, on every page,
/// the mark that it is a test without fiscal validity.
/// </summary>
/// <remarks>
/// The amounts come in minor units with the decimals of their currency, as the document carries them, and are written
/// with the ISO code of the currency and the separators of the language; the dates, in ISO 8601.
/// </remarks>
internal static class SimulatedGraphicRepresentation
{
    // The columns of the table of lines; together they fill a row of the page (PlainPdf.Columns).
    private const int CodeWidth = 12;
    private const int DescriptionWidth = 31;
    private const int QuantityWidth = 11;
    private const int UnitPriceWidth = 15;
    private const int DiscountWidth = 12;
    private const int AmountWidth = 15;

    // The columns of the totals: the label ends where the amount begins.
    private const int TotalLabelWidth = 66;
    private const int TotalAmountWidth = PlainPdf.Columns - TotalLabelWidth;

    /// <summary>
    /// Draws the document.
    /// </summary>
    /// <param name="texts">The texts of the language.</param>
    /// <param name="issuer">The issuer.</param>
    /// <param name="document">The document as it was received.</param>
    /// <param name="number">Its number.</param>
    /// <param name="cufe">Its simulated CUFE.</param>
    /// <param name="issueDate">Its issue date.</param>
    /// <returns>The bytes of the PDF.</returns>
    public static byte[] Render(SimulatedTexts texts, ProviderIssuer issuer, ProviderDocument document, DocumentNumber number, string cufe, LocalDate issueDate)
    {
        var content = document.Content;
        var format = texts.NumberFormat();
        var type = texts.NameOf(document.Type);
        var pdf = new PlainPdf($"{type} {number}", texts.Mark, texts.Watermark, texts.Page);

        string Money(long amount) => SimulatedGraphicRepresentation.Money(amount, content.Totals, format);

        pdf.Text(issuer.LegalName, bold: true);
        pdf.Text($"{texts.TaxId}: {issuer.TaxId}");
        pdf.Text($"{texts.Address}: {AddressOf(issuer.Address)}");
        pdf.Text($"{texts.Email}: {issuer.Email}   {texts.Phone}: {issuer.Phone}");
        pdf.Rule();

        pdf.Text($"{type} {number}".ToUpperInvariant(), bold: true);
        pdf.Text($"{texts.IssueDate}: {Date(issueDate)}   {texts.DueDate}: {Date(content.DueDateOn(issueDate))}");

        if (document.NumberingRange is { } range)
        {
            pdf.Text(texts.Resolution
                .Replace("{number}", range.ResolutionNumber, StringComparison.Ordinal)
                .Replace("{prefix}", range.Prefix, StringComparison.Ordinal)
                .Replace("{from}", range.From.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{to}", range.To.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{validFrom}", Date(range.ValidFrom), StringComparison.Ordinal)
                .Replace("{validUntil}", Date(range.ValidUntil), StringComparison.Ordinal));
        }

        pdf.Text($"{texts.PaymentForm}: {content.PaymentMeans.FormCode}   {texts.PaymentMethod}: {content.PaymentMeans.MethodCode}");

        if (content.BillingReference is { } reference)
        {
            pdf.Text($"{texts.Corrects}: {reference.Prefix}{reference.Consecutive.ToString(CultureInfo.InvariantCulture)} ({Date(reference.IssueDate)})   {texts.CorrectionConcept}: {content.CorrectionConceptCode}");
            pdf.Text(reference.Cufe);
        }

        pdf.Rule();

        var buyer = content.Buyer;

        pdf.Text(texts.Buyer.ToUpperInvariant(), bold: true);
        pdf.Text($"{texts.Name}: {buyer.Name}");
        pdf.Text($"{texts.Identification} ({buyer.IdentificationTypeCode}): {buyer.IdentificationNumber}{(string.IsNullOrWhiteSpace(buyer.CheckDigit) ? string.Empty : $"-{buyer.CheckDigit}")}");

        if (buyer.Address is not null)
        {
            pdf.Text($"{texts.Address}: {AddressOf(buyer.Address)}");
        }

        if (!string.IsNullOrWhiteSpace(buyer.Email) || !string.IsNullOrWhiteSpace(buyer.Phone))
        {
            pdf.Text($"{texts.Email}: {buyer.Email}   {texts.Phone}: {buyer.Phone}");
        }

        if (content.PointOfSale is { } pointOfSale)
        {
            pdf.Rule();
            pdf.Text(texts.Register.ToUpperInvariant(), bold: true);
            pdf.Text($"{texts.Register}: {pointOfSale.RegisterCode}   {texts.RegisterType}: {pointOfSale.RegisterType}   {texts.SaleCode}: {pointOfSale.SaleCode}");
            pdf.Text($"{texts.Location}: {pointOfSale.LocationName}, {pointOfSale.LocationAddress}");
            pdf.Text($"{texts.Cashier}: {pointOfSale.CashierName}");
        }

        pdf.Rule();
        pdf.Text(string.Concat(Left(texts.Code, CodeWidth), Left(texts.Description, DescriptionWidth), Right(texts.Quantity, QuantityWidth), Right(texts.UnitPrice, UnitPriceWidth), Right(texts.Discount, DiscountWidth), Right(texts.Total, AmountWidth)), bold: true);

        foreach (var line in content.Lines)
        {
            var description = PlainPdf.Wrap(line.Description, DescriptionWidth - 1);

            pdf.Text(string.Concat(Left(line.Code, CodeWidth), Left(description[0], DescriptionWidth), Right(line.Quantity.ToString("#,0.####", format), QuantityWidth), Right(Money(line.UnitPrice), UnitPriceWidth), Right(Money(line.DiscountAmount), DiscountWidth), Right(Money(line.Amount), AmountWidth)));

            foreach (var continuation in description.Skip(1))
            {
                pdf.Text(string.Concat(new string(' ', CodeWidth), continuation));
            }
        }

        pdf.Rule();

        TaxesOf(pdf, texts.Taxes, content.Lines.SelectMany(x => x.Taxes ?? []), texts, format, Money);
        TaxesOf(pdf, texts.Withholdings, content.Lines.SelectMany(x => x.Withholdings ?? []), texts, format, Money);

        var totals = content.Totals;

        TotalOf(pdf, texts.GrossAmount, Money(totals.GrossAmount), totals.Currency);
        TotalOf(pdf, texts.Discounts, Money(totals.DiscountAmount), totals.Currency);
        TotalOf(pdf, texts.Subtotal, Money(totals.Subtotal), totals.Currency);
        TotalOf(pdf, texts.TaxTotal, Money(totals.TaxAmount), totals.Currency);
        TotalOf(pdf, texts.WithholdingTotal, Money(totals.WithholdingAmount), totals.Currency);
        TotalOf(pdf, texts.Prepayments, Money(totals.PrepaymentAmount), totals.Currency);
        TotalOf(pdf, texts.Payable.ToUpperInvariant(), Money(totals.PayableAmount), totals.Currency, bold: true);

        if (content.Notes is { Count: > 0 } notes)
        {
            pdf.Rule();
            pdf.Text(texts.Notes.ToUpperInvariant(), bold: true);

            foreach (var note in notes)
            {
                pdf.Text(note);
            }
        }

        pdf.Rule();
        pdf.Text(texts.Cufe, bold: true);
        pdf.Text(cufe);
        pdf.Blank();
        pdf.Text(texts.Disclaimer);

        return pdf.Build();
    }

    /// <summary>
    /// An amount in minor units as a person reads it: with the decimals of its currency and the separators of the language.
    /// </summary>
    /// <param name="amount">The amount, in minor units.</param>
    /// <param name="totals">The totals of the document, which say the decimals of its currency.</param>
    /// <param name="format">The separators of the language.</param>
    /// <returns>The amount, without the code of the currency.</returns>
    public static string Money(long amount, DocumentTotals totals, NumberFormatInfo format)
    {
        var digits = Math.Max((short)0, totals.CurrencyDecimalDigits);
        var units = new decimal(amount);

        for (var i = 0; i < digits; i++)
        {
            units /= 10;
        }

        return units.ToString($"N{digits.ToString(CultureInfo.InvariantCulture)}", format);
    }

    /// <summary>
    /// The taxes of the lines, one row per tax and rate.
    /// </summary>
    private static void TaxesOf(PlainPdf pdf, string title, IEnumerable<LineTax> taxes, SimulatedTexts texts, NumberFormatInfo format, Func<long, string> money)
    {
        var groups = taxes.Where(x => x is not null).GroupBy(x => (x.Code, x.Name, x.RateBasisPoints)).ToList();

        if (groups.Count == 0)
        {
            return;
        }

        pdf.Text(title.ToUpperInvariant(), bold: true);

        foreach (var group in groups)
        {
            // The rate comes in basis points: 1900 is 19 %.
            var rate = (group.Key.RateBasisPoints / 100m).ToString("0.####", format);

            pdf.Text($"{Left($"{group.Key.Name} ({group.Key.Code}) {rate} %", CodeWidth + DescriptionWidth)}{texts.Base}: {money(group.Sum(x => x.BaseAmount))}   {texts.Amount}: {money(group.Sum(x => x.Amount))}");
        }

        pdf.Rule();
    }

    private static void TotalOf(PlainPdf pdf, string label, string amount, string currency, bool bold = false) => pdf.Text(string.Concat(Right(label, TotalLabelWidth), Right($"{currency} {amount}", TotalAmountWidth)), bold);

    private static string AddressOf(PostalAddress address) => string.Join(", ", new[] { address.Street, address.CityName, address.RegionName, address.CountryName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Date(LocalDate date) => date.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A text in a column aligned to the left, with a space after it; cut with an ellipsis when it does not fit.
    /// </summary>
    private static string Left(string? text, int width) => Fit(text, width - 1).PadRight(width);

    /// <summary>
    /// A text in a column aligned to the right, with a space before it.
    /// </summary>
    private static string Right(string? text, int width) => Fit(text, width - 1).PadLeft(width);

    private static string Fit(string? text, int width)
    {
        var value = text ?? string.Empty;

        return value.Length <= width ? value : string.Concat(value.AsSpan(0, width - 1), "…");
    }
}
