namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class DocumentContentTest
{
    private static string CodeOf(Action action) => Assert.Throws<CodeDesignPlusException>(action).Code;

    [Fact]
    public void EnsureValid_TotalsThatAddUp_IsAccepted()
    {
        // Act
        var exception = Record.Exception(() => TestData.InvoiceContent().EnsureValid(ElectronicDocumentType.Invoice));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_GrossThatDoesNotAddUp_ThrowsTotalsDoNotAddUp()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Totals = TestData.Totals() with { GrossAmount = 1_999_999 } };

        // Act & Assert
        Assert.Equal(Errors.TotalsDoNotAddUp.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_LineAmountThatIsNotGrossMinusDiscount_ThrowsTotalsDoNotAddUp()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Lines = [TestData.Line() with { Amount = 1_900_000 }] };

        // Act & Assert
        Assert.Equal(Errors.TotalsDoNotAddUp.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_TaxesThatDoNotAddUp_ThrowsTotalsDoNotAddUp()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Totals = TestData.Totals() with { TaxAmount = 380_001, PayableAmount = 2_380_001 } };

        // Act & Assert
        Assert.Equal(Errors.TotalsDoNotAddUp.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_WithholdingsDoNotLowerThePayable_IsAccepted()
    {
        // Arrange: a withholding of 2,5 % on the base; the payable stays gross plus taxes.
        var line = TestData.Line() with { Withholdings = [new LineTax("06", "ReteFuente", 250, 2_000_000, 50_000)] };
        var content = TestData.InvoiceContent() with { Lines = [line], Totals = TestData.Totals() with { WithholdingAmount = 50_000 } };

        // Act
        var exception = Record.Exception(() => content.EnsureValid(ElectronicDocumentType.Invoice));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureValid_PrepaymentsThatDoNotAddUp_ThrowsTotalsDoNotAddUp()
    {
        // Arrange
        var content = TestData.InvoiceContent() with
        {
            Prepayments = [new Prepayment("credit-1", 100_000, new LocalDate(2026, 11, 1))],
            Totals = TestData.Totals() with { PrepaymentAmount = 90_000, PayableAmount = 2_290_000 }
        };

        // Act & Assert
        Assert.Equal(Errors.TotalsDoNotAddUp.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_PrepaymentsThatAddUp_LowerThePayable()
    {
        // Arrange
        var content = TestData.InvoiceContent() with
        {
            Prepayments = [new Prepayment("credit-1", 100_000, new LocalDate(2026, 11, 1))],
            Totals = TestData.Totals() with { PrepaymentAmount = 100_000, PayableAmount = 2_280_000 }
        };

        // Act
        var exception = Record.Exception(() => content.EnsureValid(ElectronicDocumentType.Invoice));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(ElectronicDocumentType.CreditNote)]
    [InlineData(ElectronicDocumentType.DebitNote)]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote)]
    public void EnsureValid_NoteWithoutReference_ThrowsNoteWithoutBillingReference(ElectronicDocumentType type)
    {
        // Act & Assert
        Assert.Equal(Errors.NoteWithoutBillingReference.GetCode(), CodeOf(() => TestData.InvoiceContent().EnsureValid(type)));
    }

    [Theory]
    [InlineData(ElectronicDocumentType.CreditNote, ElectronicDocumentType.Invoice)]
    [InlineData(ElectronicDocumentType.DebitNote, ElectronicDocumentType.Invoice)]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote, ElectronicDocumentType.PosTicket)]
    public void CorrectedTypeOf_Note_IsTheTypeItCorrects(ElectronicDocumentType note, ElectronicDocumentType corrected)
    {
        // Act & Assert
        Assert.Equal(corrected, DocumentContent.CorrectedTypeOf(note));
    }

    [Fact]
    public void CorrectedTypeOf_NotANote_IsNull()
    {
        // Act & Assert
        Assert.Null(DocumentContent.CorrectedTypeOf(ElectronicDocumentType.PosTicket));
    }

    [Fact]
    public void EnsureValid_NoteWithoutCorrectionConcept_ThrowsNoteWithoutBillingReference()
    {
        // Arrange
        var content = TestData.NoteContent() with { CorrectionConceptCode = null };

        // Act & Assert
        Assert.Equal(Errors.NoteWithoutBillingReference.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.CreditNote)));
    }

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice)]
    [InlineData(ElectronicDocumentType.PosTicket)]
    public void EnsureValid_ReferenceOutsideANote_ThrowsBillingReferenceOnlyInNotes(ElectronicDocumentType type)
    {
        // Act & Assert
        Assert.Equal(Errors.BillingReferenceOnlyInNotes.GetCode(), CodeOf(() => TestData.NoteContent().EnsureValid(type)));
    }

    [Fact]
    public void EnsureValid_QuantityOfZero_ThrowsQuantityMustBePositive()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Lines = [TestData.Line() with { Quantity = 0 }] };

        // Act & Assert
        Assert.Equal(Errors.QuantityMustBePositive.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_NegativeUnitPrice_ThrowsAmountCannotBeNegative()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Lines = [TestData.Line() with { UnitPrice = -1 }] };

        // Act & Assert
        Assert.Equal(Errors.AmountCannotBeNegative.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void EnsureValid_CurrencyWithMoreThanFourDecimals_ThrowsCurrencyIsInvalid()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Totals = TestData.Totals() with { CurrencyDecimalDigits = 5 } };

        // Act & Assert
        Assert.Equal(Errors.CurrencyIsInvalid.GetCode(), CodeOf(() => content.EnsureValid(ElectronicDocumentType.Invoice)));
    }

    [Fact]
    public void GrossAmount_FractionalQuantity_RoundsHalfAwayFromZero()
    {
        // Arrange: 1,5 units of 0,01 is 0,015, which rounds up to 2 minor units.
        var line = TestData.Line() with { Quantity = 1.5m, UnitPrice = 1 };

        // Act
        var gross = line.GrossAmount;

        // Assert
        Assert.Equal(2, gross);
    }

    [Fact]
    public void Canonical_SameContent_IsTheSame()
    {
        // Act & Assert
        Assert.Equal(TestData.InvoiceContent().Canonical(), TestData.InvoiceContent().Canonical());
    }

    [Fact]
    public void Canonical_ABuyerThatDiffers_IsDifferent()
    {
        // Act & Assert
        Assert.NotEqual(TestData.InvoiceContent().Canonical(), TestData.InvoiceContent("900000009").Canonical());
    }

    [Fact]
    public void Canonical_ASeparatorInsideAText_DoesNotMakeTwoContentsEqual()
    {
        // Arrange: the same characters split differently between two fields.
        var a = TestData.InvoiceContent() with { Notes = ["a|b", "c"] };
        var b = TestData.InvoiceContent() with { Notes = ["a", "b|c"] };

        // Act & Assert
        Assert.NotEqual(a.Canonical(), b.Canonical());
    }

    [Fact]
    public void DueDateOn_WithoutADueDate_IsTheDayOfIssue()
    {
        // Arrange: a cash sale.
        var content = TestData.InvoiceContent() with { DueDate = null };

        // Act
        var due = content.DueDateOn(new LocalDate(2026, 11, 10));

        // Assert
        Assert.Equal(new LocalDate(2026, 11, 10), due);
    }

    [Fact]
    public void DueDateOn_DueAfterTheDayOfIssue_IsItsDueDate()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { DueDate = new LocalDate(2026, 12, 10) };

        // Act
        var due = content.DueDateOn(new LocalDate(2026, 11, 10));

        // Assert
        Assert.Equal(new LocalDate(2026, 12, 10), due);
    }

    [Fact]
    public void DueDateOn_DueBeforeTheDayOfIssue_IsTheDayOfIssue()
    {
        // Arrange: SETP991148967 was dated on the payment (the 9th) and issued on the 10th.
        var content = TestData.InvoiceContent() with { DueDate = new LocalDate(2026, 11, 9) };

        // Act
        var due = content.DueDateOn(new LocalDate(2026, 11, 10));

        // Assert
        Assert.Equal(new LocalDate(2026, 11, 10), due);
    }

    [Fact]
    public void EnsureNotDueBefore_DueBeforeTheDay_ThrowsDueDateBeforeIssueDate()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { DueDate = new LocalDate(2026, 11, 9) };

        // Act & Assert
        Assert.Equal(Errors.DueDateBeforeIssueDate.GetCode(), CodeOf(() => content.EnsureNotDueBefore(new LocalDate(2026, 11, 10))));
    }

    [Fact]
    public void EnsureNotDueBefore_WithoutADueDate_IsAccepted()
    {
        // Act
        var exception = Record.Exception(() => (TestData.InvoiceContent() with { DueDate = null }).EnsureNotDueBefore(new LocalDate(2026, 11, 10)));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Canonical_ADueDateThatDiffers_IsDifferent()
    {
        // Act & Assert
        Assert.NotEqual((TestData.InvoiceContent() with { DueDate = null }).Canonical(), (TestData.InvoiceContent() with { DueDate = new LocalDate(2026, 11, 10) }).Canonical());
    }
}
