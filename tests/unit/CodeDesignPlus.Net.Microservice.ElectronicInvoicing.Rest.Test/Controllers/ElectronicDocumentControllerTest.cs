namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Test.Controllers;

public class ElectronicDocumentControllerTest
{
    private readonly Mock<IMediator> mediator = new();

    private readonly Mock<IMapper> mapper = new();

    private readonly ElectronicDocumentController controller;

    public ElectronicDocumentControllerTest()
    {
        controller = new ElectronicDocumentController(mediator.Object, mapper.Object);
    }

    [Fact]
    public async Task GetElectronicDocumentById_ExistingDocument_ReturnsItInOk()
    {
        // Arrange
        var document = new ElectronicDocumentDto { Id = Guid.NewGuid(), Tenant = Guid.NewGuid(), Reference = "orders:order:1" };

        mediator.Setup(x => x.Send(It.Is<GetElectronicDocumentByIdQuery>(query => query.Id == document.Id), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var result = await controller.GetElectronicDocumentById(document.Id, CancellationToken.None);

        // Assert
        Assert.Same(document, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task CreateElectronicDocument_ValidData_SendsTheCommandAndReturnsNoContent()
    {
        // Arrange
        var data = new CreateElectronicDocumentDto { Id = Guid.NewGuid(), Reference = "orders:order:1" };
        var command = new CreateElectronicDocumentCommand(data.Id, data.Reference);

        mapper.Setup(x => x.Map<CreateElectronicDocumentCommand>(data)).Returns(command);

        // Act
        var result = await controller.CreateElectronicDocument(data, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<NoContentResult>(result);
    }
}
