namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class ElectronicDocumentAggregateTest
{
    private static readonly Instant Now = TestData.Now;

    private static readonly DocumentNumber Number = new("SETP", 990000001);

    private static ElectronicDocumentAggregate Sent()
    {
        var document = TestData.Document();

        document.AssignNumber(Number, Guid.NewGuid());
        document.Claim(Now, Now + Duration.FromMinutes(3));
        document.MarkSent("provider-1", "Procesando", null, false, Now, Now + Duration.FromMinutes(1));

        return document;
    }

    private static ElectronicDocumentAggregate Rejected()
    {
        var document = Sent();

        document.Reject(RejectionOrigin.Dian, ["Regla FAD06: el NIT no corresponde."], null, "Rechazada", Now);
        document.GetAndClearEvents();

        return document;
    }

    [Fact]
    public void Create_ValidContent_IsADraftDueNow()
    {
        // Act
        var document = ElectronicDocumentAggregate.Create(Guid.NewGuid(), TestData.Tenant, Guid.NewGuid(), ElectronicDocumentType.Invoice, "orders:order:1", TestData.InvoiceContent(), null, Now, TestData.User);

        // Assert
        Assert.Equal((ElectronicDocumentStatus.Draft, (Instant?)Now, 0), (document.Status, document.NextCheckAt, document.Attempts));
    }

    [Fact]
    public void Create_ValidContent_QueuesTheDocument()
    {
        // Arrange
        var preceding = Guid.NewGuid();

        // Act
        var queued = ElectronicDocumentAggregate.Create(Guid.NewGuid(), TestData.Tenant, Guid.NewGuid(), ElectronicDocumentType.Invoice, "orders:order:1", TestData.InvoiceContent(), preceding, Now, TestData.User).GetAndClearEvents().Single() as ElectronicDocumentQueuedDomainEvent;

        // Assert
        Assert.Equal((TestData.Tenant, ElectronicDocumentType.Invoice, "orders:order:1", (Guid?)preceding), (queued!.Tenant, queued.Type, queued.Reference, queued.PrecededBy));
    }

    [Fact]
    public void Create_ItPrecedesItself_ThrowsDocumentCannotPrecedeItself()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(id, TestData.Tenant, Guid.NewGuid(), ElectronicDocumentType.Invoice, "orders:order:1", TestData.InvoiceContent(), id, Now, TestData.User));

        // Assert
        Assert.Equal(Errors.DocumentCannotPrecedeItself.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_ReferenceLongerThanTitle_ThrowsReferenceIsTooLong()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.NewGuid(), TestData.Tenant, Guid.NewGuid(), ElectronicDocumentType.Invoice, new string('r', FieldLength.Title + 1), TestData.InvoiceContent(), null, Now, TestData.User));

        // Assert
        Assert.Equal(Errors.ReferenceIsTooLong.GetCode(), exception.Code);
    }

    [Fact]
    public void ContentHashOf_SameRequest_IsTheSame()
    {
        // Act & Assert
        Assert.Equal(TestData.Document().ContentHash, ElectronicDocumentAggregate.ContentHashOf(ElectronicDocumentType.Invoice, "orders:order:1", null, TestData.InvoiceContent()));
    }

    [Fact]
    public void ContentHashOf_AnotherReference_IsDifferent()
    {
        // Act & Assert
        Assert.NotEqual(TestData.Document().ContentHash, ElectronicDocumentAggregate.ContentHashOf(ElectronicDocumentType.Invoice, "orders:order:2", null, TestData.InvoiceContent()));
    }

    [Fact]
    public void AssignNumber_AlreadyNumbered_KeepsTheFirstNumber()
    {
        // Arrange
        var document = TestData.Document();

        document.AssignNumber(Number, Guid.NewGuid());

        // Act
        var assigned = document.AssignNumber(new DocumentNumber("SETP", 990000002), Guid.NewGuid());

        // Assert
        Assert.Equal((false, Number), (assigned, document.Number));
    }

    [Fact]
    public void Claim_Due_HoldsTheDocumentUntilTheLease()
    {
        // Arrange
        var document = TestData.Document();
        var lease = Now + Duration.FromMinutes(3);

        // Act
        var claimed = document.Claim(Now, lease);

        // Assert
        Assert.Equal((true, (Instant?)lease), (claimed, document.LeasedUntil));
    }

    [Fact]
    public void Claim_HeldByAnotherRound_IsRefusedEvenIfBroughtForward()
    {
        // Arrange
        var document = TestData.Document();

        document.Claim(Now, Now + Duration.FromMinutes(3));
        document.Expedite(Now, TestData.User);

        // Act
        var claimed = document.Claim(Now + Duration.FromSeconds(10), Now + Duration.FromMinutes(4));

        // Assert
        Assert.False(claimed);
    }

    [Fact]
    public void Claim_NotDueYet_IsRefused()
    {
        // Arrange
        var document = Sent();

        // Act
        var claimed = document.Claim(Now, Now + Duration.FromMinutes(3));

        // Assert
        Assert.False(claimed);
    }

    [Fact]
    public void MarkSent_FirstSend_RecordsTheRoundAndReleasesTheLease()
    {
        // Act
        var document = Sent();

        // Assert
        Assert.Equal((ElectronicDocumentStatus.Sent, "provider-1", 1, (Instant?)Now, (Instant?)null), (document.Status, document.ProviderDocumentId, document.Attempts, document.FirstSentAt, document.LeasedUntil));
    }

    [Fact]
    public void Validate_Sent_IsFinalAndAnnouncesTheCufe()
    {
        // Arrange
        var document = Sent();

        // Act
        document.Validate("provider-1", "cufe-1", null, new LocalDate(2026, 11, 4), "Aprobada", null, Now);

        // Assert
        var validated = Assert.IsType<ElectronicDocumentValidatedDomainEvent>(document.GetAndClearEvents().Single());

        Assert.Equal((ElectronicDocumentStatus.Validated, "cufe-1", "SETP", 990000001L, (Instant?)null), (document.Status, validated.Cufe, validated.Prefix, validated.Consecutive, document.NextCheckAt));
    }

    [Fact]
    public void Validate_AlreadyValidated_ThrowsDocumentAlreadyValidated()
    {
        // Arrange
        var document = Sent();

        document.Validate("provider-1", "cufe-1", null, null, "Aprobada", null, Now);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => document.Validate("provider-1", "cufe-2", null, null, "Aprobada", null, Now));

        // Assert
        Assert.Equal(Errors.DocumentAlreadyValidated.GetCode(), exception.Code);
    }

    [Fact]
    public void Validate_WithoutNumber_ThrowsNumberIsRequired()
    {
        // Arrange
        var document = TestData.Document();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => document.Validate("provider-1", "cufe-1", null, null, "Aprobada", null, Now));

        // Assert
        Assert.Equal(Errors.NumberIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Reject_Sent_KeepsTheReasonsAsTheyCame()
    {
        // Arrange
        var document = Sent();

        // Act
        document.Reject(RejectionOrigin.Dian, ["Regla FAD06: el NIT no corresponde."], null, "Rechazada", Now);

        // Assert
        var rejected = Assert.IsType<ElectronicDocumentRejectedDomainEvent>(document.GetAndClearEvents().Single());

        Assert.Equal((ElectronicDocumentStatus.Rejected, RejectionOrigin.Dian, "Regla FAD06: el NIT no corresponde."), (document.Status, rejected.Rejection.Origin, rejected.Rejection.Reasons.Single()));
    }

    [Fact]
    public void Correct_Rejected_GoesBackToDraftOnTheSameNumber()
    {
        // Arrange
        var document = Rejected();
        var corrected = TestData.InvoiceContent("900000009");

        // Act
        document.Correct(corrected, Now, TestData.User);

        // Assert
        Assert.Equal((ElectronicDocumentStatus.Draft, Number, 0, (DocumentRejection?)null, corrected), (document.Status, document.Number, document.Attempts, document.Rejection, document.Content));
    }

    [Fact]
    public void Correct_Rejected_QueuesTheDocumentAgainWithItsNewHash()
    {
        // Arrange
        var document = Rejected();
        var before = document.ContentHash;

        // Act
        document.Correct(TestData.InvoiceContent("900000009"), Now, TestData.User);

        // Assert
        Assert.Equal((true, true), (document.GetAndClearEvents().Single() is ElectronicDocumentQueuedDomainEvent, before != document.ContentHash));
    }

    [Fact]
    public void Correct_Rejected_CountsANewRevisionEvenWithTheSameContent()
    {
        // Arrange
        var document = Rejected();

        // Act
        document.Correct(document.Content, Now, TestData.User);

        // Assert
        Assert.Equal(1, document.Revision);
    }

    [Fact]
    public void Correct_NotRejected_ThrowsOnlyRejectedDocumentsCanBeCorrected()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Sent().Correct(TestData.InvoiceContent(), Now, TestData.User));

        // Assert
        Assert.Equal(Errors.OnlyRejectedDocumentsCanBeCorrected.GetCode(), exception.Code);
    }

    [Fact]
    public void Stall_Waiting_AnnouncesItOnce()
    {
        // Arrange
        var document = Sent();

        // Act
        var first = document.Stall(Now);
        var second = document.Stall(Now);

        // Assert
        Assert.Equal((true, false, 1), (first, second, document.GetAndClearEvents().OfType<ElectronicDocumentStalledDomainEvent>().Count()));
    }

    [Fact]
    public void Expedite_Stalled_RestartsTheRetries()
    {
        // Arrange
        var document = Sent();

        document.Stall(Now);

        // Act
        var expedited = document.Expedite(Now, TestData.User);

        // Assert
        Assert.Equal((true, 0, (Instant?)null, (Instant?)Now), (expedited, document.Attempts, document.StalledAt, document.NextCheckAt));
    }

    [Fact]
    public void Expedite_Final_DoesNothing()
    {
        // Act
        var expedited = Rejected().Expedite(Now, TestData.User);

        // Assert
        Assert.False(expedited);
    }
}
