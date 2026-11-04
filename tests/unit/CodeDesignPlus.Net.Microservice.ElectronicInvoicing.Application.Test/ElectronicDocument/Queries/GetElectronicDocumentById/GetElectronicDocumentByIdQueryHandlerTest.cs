using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument.Queries.GetElectronicDocumentById;

public class GetElectronicDocumentByIdQueryHandlerTest
{
    private readonly Mock<IElectronicDocumentRepository> repository = new();

    private readonly Mock<IUserContext> user = new();

    private readonly GetElectronicDocumentByIdQueryHandler handler;

    private readonly Guid tenant = Guid.NewGuid();

    public GetElectronicDocumentByIdQueryHandlerTest()
    {
        MapsterConfigElectronicInvoicing.Configure();

        user.Setup(x => x.Tenant).Returns(tenant);

        handler = new GetElectronicDocumentByIdQueryHandler(repository.Object, user.Object, new Mapper(TypeAdapterConfig.GlobalSettings));
    }

    [Fact]
    public async Task Handle_RequestIsNull_ThrowsInvalidRequest()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal(Errors.InvalidRequest.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_DocumentOfAnotherTenantOrMissing_ThrowsElectronicDocumentNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(id, tenant, It.IsAny<CancellationToken>())).ReturnsAsync((ElectronicDocumentAggregate)null!);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new GetElectronicDocumentByIdQuery(id), CancellationToken.None));

        // Assert
        Assert.Equal(Errors.ElectronicDocumentNotFound.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_DocumentOfTheTenant_ReturnsIt()
    {
        // Arrange
        var document = ElectronicDocumentAggregate.Create(Guid.NewGuid(), tenant, "orders:order:1", Guid.NewGuid());

        repository.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(document.Id, tenant, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var result = await handler.Handle(new GetElectronicDocumentByIdQuery(document.Id), CancellationToken.None);

        // Assert
        Assert.Equal((document.Id, tenant, document.Reference), (result.Id, result.Tenant, result.Reference));
    }
}
