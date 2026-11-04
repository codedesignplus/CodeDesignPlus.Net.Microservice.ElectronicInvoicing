using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument.Queries.GetElectronicDocumentById;

public class GetElectronicDocumentByIdQueryHandlerTest
{
    private readonly Mock<IElectronicDocumentRepository> repository = new();

    private readonly Mock<IUserContext> user = new();

    private readonly GetElectronicDocumentByIdQueryHandler handler;

    public GetElectronicDocumentByIdQueryHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);

        handler = new GetElectronicDocumentByIdQueryHandler(repository.Object, user.Object);
    }

    [Fact]
    public async Task Handle_DocumentOfAnotherTenant_ThrowsElectronicDocumentNotFound()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new GetElectronicDocumentByIdQuery(Guid.NewGuid()), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.ElectronicDocumentNotFound.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_TenantInTheRequest_LooksInsideThatTenant()
    {
        // Arrange
        var other = Guid.NewGuid();
        var document = TestData.Document(tenant: other);

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, other, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var dto = await handler.Handle(new GetElectronicDocumentByIdQuery(document.Id, other), CancellationToken.None);

        // Assert
        Assert.Equal((document.Id, other, ElectronicDocumentStatus.Draft, "orders:order:1"), (dto.Id, dto.Tenant, dto.Status, dto.Reference));
    }

    [Fact]
    public async Task Handle_ValidatedDocument_ShowsItsNumberAndCufe()
    {
        // Arrange
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 7), Guid.NewGuid());
        document.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));
        document.Validate("provider-1", "cufe-7", null, new LocalDate(2026, 11, 4), "Aprobada", null, TestData.Now);
        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var dto = await handler.Handle(new GetElectronicDocumentByIdQuery(document.Id), CancellationToken.None);

        // Assert
        Assert.Equal(("SETP", (long?)7, "cufe-7"), (dto.Prefix, dto.Consecutive, dto.Cufe));
    }
}
