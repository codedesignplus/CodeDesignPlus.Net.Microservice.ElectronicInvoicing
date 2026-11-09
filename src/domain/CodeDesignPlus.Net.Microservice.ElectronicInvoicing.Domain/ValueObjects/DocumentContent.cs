namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// What an electronic document says, in neutral terms: the buyer, the lines, the totals and, in a note, the document it
/// corrects. The codes are the ones of the tax authority's lists; each adapter translates them to its provider.
/// </summary>
/// <param name="Buyer">Who the document is issued to.</param>
/// <param name="Lines">What is sold.</param>
/// <param name="Totals">The totals, which must add up line by line.</param>
/// <param name="Prepayments">Prepayments applied to the document.</param>
/// <param name="PaymentMeans">How it is paid.</param>
/// <param name="DueDate">When it is due, as a calendar date of the issuer.</param>
/// <param name="Notes">Free notes printed on the document.</param>
/// <param name="BillingReference">In a note, the document it corrects.</param>
/// <param name="CorrectionConceptCode">In a note, the code of the correction concept in the tax authority's list.</param>
public sealed record DocumentContent(Buyer Buyer, List<DocumentLine> Lines, DocumentTotals Totals, List<Prepayment> Prepayments, PaymentMeans PaymentMeans, LocalDate DueDate, List<string> Notes, BillingReference? BillingReference, string? CorrectionConceptCode)
{
    /// <summary>
    /// The highest number of decimal digits a currency has (ISO 4217 has none above four).
    /// </summary>
    private const short MaxDecimalDigits = 4;

    /// <summary>
    /// Whether the document type is a note that corrects another document.
    /// </summary>
    /// <param name="type">The type of the document.</param>
    /// <returns>True for credit and debit notes and for the adjustment notes of a point of sale ticket.</returns>
    public static bool IsNote(ElectronicDocumentType type) => type is ElectronicDocumentType.CreditNote or ElectronicDocumentType.DebitNote or ElectronicDocumentType.PosAdjustmentNote;

    /// <summary>
    /// The type of the document a note corrects: the credit and debit notes correct an invoice, and the adjustment note
    /// corrects a point of sale ticket.
    /// </summary>
    /// <param name="type">The type of the note.</param>
    /// <returns>The type it corrects, or null when the type is not a note.</returns>
    public static ElectronicDocumentType? CorrectedTypeOf(ElectronicDocumentType type) => type switch
    {
        ElectronicDocumentType.CreditNote or ElectronicDocumentType.DebitNote => ElectronicDocumentType.Invoice,
        ElectronicDocumentType.PosAdjustmentNote => ElectronicDocumentType.PosTicket,
        _ => null
    };

    /// <summary>
    /// Checks that the content is complete for its type and that the totals add up line by line.
    /// </summary>
    /// <param name="type">The type of the document.</param>
    public void EnsureValid(ElectronicDocumentType type)
    {
        DomainGuard.IsNull(Buyer, Errors.BuyerIsRequired);
        DomainGuard.IsNullOrWhiteSpace(Buyer.IdentificationNumber, Errors.BuyerIsRequired);
        DomainGuard.IsNullOrWhiteSpace(Buyer.Name, Errors.BuyerIsRequired);
        DomainGuard.IsNull(Lines, Errors.LinesAreRequired);
        DomainGuard.IsEmpty(Lines, Errors.LinesAreRequired);
        DomainGuard.IsNull(Totals, Errors.TotalsDoNotAddUp);
        DomainGuard.IsNull(PaymentMeans, Errors.PaymentMeansIsRequired);
        DomainGuard.IsTrue(DueDate == default, Errors.DueDateIsRequired);
        DomainGuard.IsTrue(string.IsNullOrWhiteSpace(Totals.Currency) || Totals.Currency.Length != 3, Errors.CurrencyIsInvalid);
        DomainGuard.IsTrue(Totals.CurrencyDecimalDigits < 0 || Totals.CurrencyDecimalDigits > MaxDecimalDigits, Errors.CurrencyIsInvalid);

        if (IsNote(type))
        {
            DomainGuard.IsNull(BillingReference, Errors.NoteWithoutBillingReference);
            DomainGuard.IsNullOrWhiteSpace(BillingReference!.Cufe, Errors.NoteWithoutBillingReference);
            DomainGuard.IsNullOrWhiteSpace(CorrectionConceptCode, Errors.NoteWithoutBillingReference);
        }
        else
        {
            DomainGuard.IsNotNull(BillingReference, Errors.BillingReferenceOnlyInNotes);
        }

        foreach (var line in Lines)
        {
            EnsureLineIsValid(line);
        }

        EnsureTotalsAddUp();
    }

    /// <summary>
    /// The content in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(
        Buyer.Canonical(),
        Canonicalizer.List(Lines.Select(x => x.Canonical())),
        Totals.Canonical(),
        Canonicalizer.List((Prepayments ?? []).Select(x => x.Canonical())),
        PaymentMeans.Canonical(),
        DueDate,
        Canonicalizer.List(Notes),
        BillingReference?.Canonical(),
        CorrectionConceptCode);

    private static void EnsureLineIsValid(DocumentLine line)
    {
        DomainGuard.IsNull(line, Errors.LinesAreRequired);
        DomainGuard.IsNullOrWhiteSpace(line.Code, Errors.LineIsIncomplete);
        DomainGuard.IsNullOrWhiteSpace(line.Description, Errors.LineIsIncomplete);
        DomainGuard.IsNullOrWhiteSpace(line.UnitCode, Errors.LineIsIncomplete);
        DomainGuard.IsTrue(line.Quantity <= 0, Errors.QuantityMustBePositive);
        DomainGuard.IsTrue(line.UnitPrice < 0 || line.DiscountAmount < 0 || line.Amount < 0, Errors.AmountCannotBeNegative);

        // The net amount of the line is the gross minus its discount: a line that does not say so would print one total and
        // charge another.
        DomainGuard.IsTrue(line.Amount != line.GrossAmount - line.DiscountAmount, Errors.TotalsDoNotAddUp);

        foreach (var tax in (line.Taxes ?? []).Concat(line.Withholdings ?? []))
        {
            DomainGuard.IsNull(tax, Errors.LineIsIncomplete);
            DomainGuard.IsNullOrWhiteSpace(tax.Code, Errors.LineIsIncomplete);
            DomainGuard.IsTrue(tax.RateBasisPoints < 0 || tax.BaseAmount < 0 || tax.Amount < 0, Errors.AmountCannotBeNegative);
        }
    }

    private void EnsureTotalsAddUp()
    {
        var prepayments = Prepayments ?? [];

        DomainGuard.IsTrue(prepayments.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Amount <= 0 || x.ReceivedOn == default), Errors.PrepaymentIsIncomplete);
        DomainGuard.IsTrue(Totals.GrossAmount < 0 || Totals.DiscountAmount < 0 || Totals.TaxAmount < 0 || Totals.WithholdingAmount < 0 || Totals.PrepaymentAmount < 0, Errors.AmountCannotBeNegative);

        var adds =
            Totals.GrossAmount == Lines.Sum(x => x.GrossAmount)
            && Totals.DiscountAmount == Lines.Sum(x => x.DiscountAmount)
            && Totals.TaxAmount == Lines.Sum(x => (x.Taxes ?? []).Sum(t => t.Amount))
            && Totals.WithholdingAmount == Lines.Sum(x => (x.Withholdings ?? []).Sum(t => t.Amount))
            && Totals.PrepaymentAmount == prepayments.Sum(x => x.Amount)
            // The withholdings are informative in the document (the buyer withholds them when paying): they do not lower
            // what it says is payable, as in the LegalMonetaryTotal of the UBL of the tax authority.
            && Totals.PayableAmount == Totals.GrossAmount - Totals.DiscountAmount + Totals.TaxAmount - Totals.PrepaymentAmount;

        DomainGuard.IsFalse(adds, Errors.TotalsDoNotAddUp);
        DomainGuard.IsTrue(Totals.PayableAmount < 0, Errors.AmountCannotBeNegative);
    }
}
