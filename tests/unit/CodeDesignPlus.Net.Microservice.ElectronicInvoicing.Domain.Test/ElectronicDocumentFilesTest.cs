using StoredFile = CodeDesignPlus.Net.ValueObjects.FileServer.File;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// A validated document whose XML is not kept asks for its files again until it has them or runs out of attempts.
/// </summary>
public class ElectronicDocumentFilesTest
{
    private static readonly Instant Now = TestData.Now;

    private static DocumentFiles WithXml() => new(StoredFile.Create(Guid.NewGuid(), "electronic-documents", "SETP990000001.xml", ".xml", "application/xml", 3), null, null);

    /// <summary>
    /// Validated with no files and no next look, as the documents validated before the files were retried.
    /// </summary>
    private static ElectronicDocumentAggregate ValidatedWithoutFiles()
    {
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 990000001), Guid.NewGuid());
        document.Validate("provider-1", "cufe-1", null, null, "Aprobada", null, Now);
        document.GetAndClearEvents();

        return document;
    }

    [Fact]
    public void Claim_ValidatedWithoutFilesNorNextLook_IsDue()
    {
        // Arrange
        var document = ValidatedWithoutFiles();

        // Act
        var claimed = document.Claim(Now, Now + Duration.FromMinutes(3));

        // Assert
        Assert.True(claimed);
    }

    [Fact]
    public void Claim_ValidatedWithItsXml_IsRefused()
    {
        // Arrange
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 990000001), Guid.NewGuid());
        document.Validate("provider-1", "cufe-1", null, null, "Aprobada", WithXml(), Now);

        // Act
        var claimed = document.Claim(Now, Now + Duration.FromMinutes(3));

        // Assert
        Assert.False(claimed);
    }

    [Fact]
    public void RecordMissingFiles_AttemptsLeft_LooksAgainAtTheNextCheck()
    {
        // Arrange
        var document = ValidatedWithoutFiles();
        var next = Now + Duration.FromMinutes(1);

        // Act
        var stalled = document.RecordMissingFiles(null, Now, next, 3);

        // Assert
        Assert.Equal((false, 1, (Instant?)next, true, "files-missing"), (stalled, document.FilesAttempts, document.NextCheckAt, document.FilesPending, document.LastCheckOutcome));
    }

    [Fact]
    public void RecordMissingFiles_LastAttempt_StallsAndAnnouncesIt()
    {
        // Arrange
        var document = ValidatedWithoutFiles();

        document.RecordMissingFiles(null, Now, Now, 2);

        // Act
        var stalled = document.RecordMissingFiles(null, Now, Now + Duration.FromMinutes(2), 2);

        // Assert
        var announced = Assert.IsType<ElectronicDocumentFilesStalledDomainEvent>(document.GetAndClearEvents().Single());

        Assert.Equal((true, (Instant?)Now, (Instant?)null, false, 2, "cufe-1"), (stalled, document.FilesStalledAt, document.NextCheckAt, document.Claim(Now, Now + Duration.FromMinutes(3)), announced.Attempts, announced.Cufe));
    }

    [Fact]
    public void KeepFiles_WithTheXml_StopsAskingForThem()
    {
        // Arrange
        var document = ValidatedWithoutFiles();

        document.RecordMissingFiles(null, Now, Now + Duration.FromMinutes(1), 3);

        // Act
        document.KeepFiles(WithXml(), Now);

        // Assert
        Assert.Equal((false, (Instant?)null, "files-stored"), (document.FilesPending, document.NextCheckAt, document.LastCheckOutcome));
    }

    [Fact]
    public void KeepFiles_WithoutTheXml_ThrowsFilesAreRequired()
    {
        // Arrange
        var document = ValidatedWithoutFiles();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => document.KeepFiles(new DocumentFiles(null, null, null), Now));

        // Assert
        Assert.Equal(Errors.FilesAreRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Expedite_FilesStalled_RestartsTheirRetries()
    {
        // Arrange
        var document = ValidatedWithoutFiles();

        document.RecordMissingFiles(null, Now, Now, 1);

        // Act
        var expedited = document.Expedite(Now, TestData.User);

        // Assert
        Assert.Equal((true, 0, (Instant?)null, (Instant?)Now), (expedited, document.FilesAttempts, document.FilesStalledAt, document.NextCheckAt));
    }

    [Fact]
    public void Expedite_ValidatedWithItsXml_DoesNothing()
    {
        // Arrange
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 990000001), Guid.NewGuid());
        document.Validate("provider-1", "cufe-1", null, null, "Aprobada", WithXml(), Now);

        // Act
        var expedited = document.Expedite(Now, TestData.User);

        // Assert
        Assert.False(expedited);
    }
}
