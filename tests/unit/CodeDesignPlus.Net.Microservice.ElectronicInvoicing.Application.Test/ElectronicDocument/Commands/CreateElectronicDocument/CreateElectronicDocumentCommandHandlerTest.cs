using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.CreateElectronicDocument;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.ElectronicDocument.Commands.CreateElectronicDocument;

public class CreateElectronicDocumentCommandHandlerTest
{
    private readonly Mock<IElectronicDocumentRepository> repository = new();

    private readonly Mock<IUserContext> user = new();

    private readonly Mock<IPubSub> pubsub = new();

    private readonly CreateElectronicDocumentCommandHandler handler;

    private readonly Guid tenant = Guid.NewGuid();

    public CreateElectronicDocumentCommandHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(tenant);
        user.Setup(x => x.IdUser).Returns(Guid.NewGuid());

        handler = new CreateElectronicDocumentCommandHandler(repository.Object, user.Object, pubsub.Object);
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
    public async Task Handle_DocumentAlreadyExists_IsTheRetryAndDoesNotCreateItAgain()
    {
        // Arrange
        var request = new CreateElectronicDocumentCommand(Guid.NewGuid(), "orders:order:1");

        repository.Setup(x => x.ExistsAsync<ElectronicDocumentAggregate>(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesTheDocumentInTheTenantOfTheUser()
    {
        // Arrange
        var request = new CreateElectronicDocumentCommand(Guid.NewGuid(), "orders:order:1");

        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        repository.Verify(x => x.CreateIfAbsentAsync(It.Is<ElectronicDocumentAggregate>(document => document.Id == request.Id && document.Tenant == tenant && document.Reference == request.Reference), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesTheCreatedEvent()
    {
        // Arrange
        var request = new CreateElectronicDocumentCommand(Guid.NewGuid(), "orders:order:1");

        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        pubsub.Verify(x => x.PublishAsync(It.Is<IReadOnlyList<IDomainEvent>>(events => events.Single() is ElectronicDocumentCreatedDomainEvent && events.Single().AggregateId == request.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConcurrentRequestSavedItFirst_PublishesNothing()
    {
        // Arrange
        var request = new CreateElectronicDocumentCommand(Guid.NewGuid(), "orders:order:1");

        repository.Setup(x => x.CreateIfAbsentAsync(It.IsAny<ElectronicDocumentAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        pubsub.Verify(x => x.PublishAsync(It.IsAny<IReadOnlyList<IDomainEvent>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
