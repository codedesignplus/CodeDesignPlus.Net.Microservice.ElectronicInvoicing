namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;

/// <summary>
/// What a caller sends to issue a document: the content in minor units, without what this service resolves (the
/// decimal digits of the currency and, for a document issued here, the data of the document a note corrects).
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
public sealed record DocumentContentInput(Buyer Buyer, List<DocumentLine> Lines, DocumentTotalsInput Totals, List<Prepayment>? Prepayments, PaymentMeans PaymentMeans, LocalDate DueDate, List<string>? Notes, BillingReferenceInput? BillingReference, string? CorrectionConceptCode)
{
    /// <summary>
    /// The content as the document keeps it.
    /// </summary>
    /// <param name="decimalDigits">The decimal digits of the currency.</param>
    /// <param name="reference">The resolved document a note corrects.</param>
    /// <returns>The content.</returns>
    public DocumentContent ToContent(short decimalDigits, BillingReference? reference) => new(
        Buyer,
        Lines ?? [],
        new DocumentTotals(Totals.Currency?.ToUpperInvariant() ?? string.Empty, decimalDigits, Totals.GrossAmount, Totals.DiscountAmount, Totals.TaxAmount, Totals.WithholdingAmount, Totals.PrepaymentAmount, Totals.PayableAmount),
        Prepayments ?? [],
        PaymentMeans,
        DueDate,
        Notes ?? [],
        reference,
        CorrectionConceptCode);
}

/// <summary>
/// The totals as the caller computed them, in minor units.
/// </summary>
/// <param name="Currency">ISO 4217 code of the currency.</param>
/// <param name="GrossAmount">Sum of quantity times unit price of every line, before discounts.</param>
/// <param name="DiscountAmount">Sum of the discounts of the lines.</param>
/// <param name="TaxAmount">Sum of the taxes of the lines.</param>
/// <param name="WithholdingAmount">Sum of the withholdings of the lines.</param>
/// <param name="PrepaymentAmount">Sum of the prepayments.</param>
/// <param name="PayableAmount">What the buyer pays: gross minus discounts plus taxes minus prepayments.</param>
public sealed record DocumentTotalsInput(string Currency, long GrossAmount, long DiscountAmount, long TaxAmount, long WithholdingAmount, long PrepaymentAmount, long PayableAmount);

/// <summary>
/// The document a note corrects: its id when it was issued here, or its number, CUFE and issue date when it was
/// issued elsewhere.
/// </summary>
/// <param name="DocumentId">The id of the corrected document in this service.</param>
/// <param name="Prefix">Prefix of a document issued elsewhere.</param>
/// <param name="Consecutive">Consecutive of a document issued elsewhere.</param>
/// <param name="Cufe">CUFE of a document issued elsewhere.</param>
/// <param name="IssueDate">Issue date of a document issued elsewhere.</param>
public sealed record BillingReferenceInput(Guid? DocumentId, string? Prefix, long? Consecutive, string? Cufe, LocalDate? IssueDate);

/// <summary>
/// The rules of a content that do not depend on the type of the document; the rest is checked by the domain.
/// </summary>
public class DocumentContentInputValidator : AbstractValidator<DocumentContentInput>
{
    /// <summary>
    /// Declares the rules.
    /// </summary>
    public DocumentContentInputValidator()
    {
        RuleFor(x => x.Buyer).NotNull();
        RuleFor(x => x.Buyer.Name).NotEmpty().MaximumLength(FieldLength.Name).When(x => x.Buyer is not null);
        RuleFor(x => x.Buyer.FirstName).MaximumLength(FieldLength.Name).When(x => x.Buyer is not null);
        RuleFor(x => x.Buyer.LastName).MaximumLength(FieldLength.Name).When(x => x.Buyer is not null);
        RuleFor(x => x.Buyer.IdentificationNumber).NotEmpty().MaximumLength(FieldLength.Name).When(x => x.Buyer is not null);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.Code).NotEmpty().MaximumLength(FieldLength.Name);
            line.RuleFor(x => x.Description).NotEmpty().MaximumLength(FieldLength.Description);
            line.RuleFor(x => x.UnitCode).NotEmpty().MaximumLength(FieldLength.Name);
        });
        RuleFor(x => x.Totals).NotNull();
        RuleFor(x => x.Totals.Currency).NotEmpty().Length(3).When(x => x.Totals is not null);
        RuleFor(x => x.PaymentMeans).NotNull();
        RuleForEach(x => x.Notes).MaximumLength(FieldLength.Notes);
    }
}
