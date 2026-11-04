namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Test.Services;

public class ElectronicInvoicingServiceTest
{
    private readonly Mock<IMediator> mediator = new();

    private readonly ElectronicInvoicingService service;

    private readonly ServerCallContext context = TestServerCallContext.Create("GetDocument", null, DateTime.UtcNow.AddMinutes(1), new Metadata(), CancellationToken.None, "127.0.0.1", null, null, (_) => Task.CompletedTask, () => new WriteOptions(), (_) => { });

    public ElectronicInvoicingServiceTest()
    {
        CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Core.Mapster.MapsterConfig.Configure();

        service = new ElectronicInvoicingService(mediator.Object, new Mapper(TypeAdapterConfig.GlobalSettings));
    }

    [Fact]
    public async Task GetDocument_InvalidId_ThrowsInvalidId()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => service.GetDocument(new GetDocumentRequest { Id = "not-a-guid" }, context));

        // Assert
        Assert.Equal(Infrastructure.Errors.InvalidId.GetCode(), exception.Code);
    }

    [Fact]
    public async Task GetDocument_ExistingDocument_MapsItToTheResponse()
    {
        // Arrange
        var document = new ElectronicDocumentDto { Id = Guid.NewGuid(), Tenant = Guid.NewGuid(), Reference = "orders:order:1" };

        mediator.Setup(x => x.Send(It.Is<GetElectronicDocumentByIdQuery>(query => query.Id == document.Id), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var response = await service.GetDocument(new GetDocumentRequest { Id = document.Id.ToString() }, context);

        // Assert
        Assert.Equal((document.Id.ToString(), document.Tenant.ToString(), document.Reference), (response.Id, response.Tenant, response.Reference));
    }
}
