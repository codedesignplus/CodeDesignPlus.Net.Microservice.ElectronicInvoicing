namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class ElectronicDocumentAggregateTest
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static readonly Guid CreatedBy = Guid.NewGuid();

    private const string Reference = "orders:order:1";

    [Fact]
    public void Create_ValidParameters_KeepsTenantAndReference()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var document = ElectronicDocumentAggregate.Create(id, Tenant, Reference, CreatedBy);

        // Assert
        Assert.Equal((id, Tenant, Reference, CreatedBy, true), (document.Id, document.Tenant, document.Reference, document.CreatedBy, document.IsActive));
    }

    [Fact]
    public void Create_ValidParameters_AddsTheCreatedEvent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var domainEvent = ElectronicDocumentAggregate.Create(id, Tenant, Reference, CreatedBy).GetAndClearEvents().Single() as ElectronicDocumentCreatedDomainEvent;

        // Assert
        Assert.Equal((id, Tenant, Reference), (domainEvent!.AggregateId, domainEvent.Tenant, domainEvent.Reference));
    }

    [Fact]
    public void Create_EmptyId_ThrowsElectronicDocumentIdIsRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.Empty, Tenant, Reference, CreatedBy));

        // Assert
        Assert.Equal(Errors.ElectronicDocumentIdIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_EmptyTenant_ThrowsTenantIsRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.NewGuid(), Guid.Empty, Reference, CreatedBy));

        // Assert
        Assert.Equal(Errors.TenantIsRequired.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_EmptyReference_ThrowsReferenceIsRequired(string? reference)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.NewGuid(), Tenant, reference!, CreatedBy));

        // Assert
        Assert.Equal(Errors.ReferenceIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_ReferenceLongerThanTitle_ThrowsReferenceIsTooLong()
    {
        // Arrange
        var reference = new string('r', FieldLength.Title + 1);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.NewGuid(), Tenant, reference, CreatedBy));

        // Assert
        Assert.Equal(Errors.ReferenceIsTooLong.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_ReferenceAsLongAsTitle_IsAccepted()
    {
        // Arrange
        var reference = new string('r', FieldLength.Title);

        // Act
        var document = ElectronicDocumentAggregate.Create(Guid.NewGuid(), Tenant, reference, CreatedBy);

        // Assert
        Assert.Equal(reference, document.Reference);
    }

    [Fact]
    public void Create_EmptyCreatedBy_ThrowsCreatedByIsRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ElectronicDocumentAggregate.Create(Guid.NewGuid(), Tenant, Reference, Guid.Empty));

        // Assert
        Assert.Equal(Errors.CreatedByIsRequired.GetCode(), exception.Code);
    }
}
