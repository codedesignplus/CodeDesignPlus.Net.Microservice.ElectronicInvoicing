using System.Text;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessProviderEvent;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Test.Controllers;

public class ElectronicDocumentControllerTest
{
    private readonly Mock<IMediator> mediator = new();

    [Fact]
    public async Task GetElectronicDocumentById_ExistingDocument_ReturnsItInOk()
    {
        // Arrange
        var document = new ElectronicDocumentDto { Id = Guid.NewGuid(), Reference = "orders:order:1" };

        mediator.Setup(x => x.Send(It.Is<GetElectronicDocumentByIdQuery>(query => query.Id == document.Id && query.Tenant == null), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var result = await new ElectronicDocumentController(mediator.Object).GetElectronicDocumentById(document.Id, CancellationToken.None);

        // Assert
        Assert.Same(document, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task RetryElectronicDocument_FromTheScreen_OnlyBringsItForward()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<RetryElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, ElectronicDocumentStatus.Sent, null, null));

        // Act
        await new ElectronicDocumentController(mediator.Object).RetryElectronicDocument(id, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<RetryElectronicDocumentCommand>(c => c.Id == id && c.Content == null && c.Tenant == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class IssuerControllerTest
{
    [Fact]
    public async Task GetIssuer_TenantWithoutIssuer_AnswersNoContent()
    {
        // Arrange
        var mediator = new Mock<IMediator>();

        mediator.Setup(x => x.Send(It.IsAny<GetIssuerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerDto?)null);

        // Act
        var result = await new IssuerController(mediator.Object).GetIssuer(CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }
}

public class ProviderEventsControllerTest
{
    private readonly Mock<IMediator> mediator = new();

    private ProviderEventsController Controller(string body)
    {
        var context = new DefaultHttpContext();

        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers["X-Invented-Signature"] = "abc";

        return new ProviderEventsController(mediator.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public async Task Receive_Call_PassesTheBodyAndTheHeadersAsTheyCame()
    {
        // Act
        var result = await Controller("""{"Id":"doc-1"}""").Receive("aliaddo", CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<ProcessProviderEventCommand>(c => c.Provider == "aliaddo" && c.Body == """{"Id":"doc-1"}""" && c.Headers["x-invented-signature"] == "abc"), It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Receive_ProviderNotAnswering_AnswersServiceUnavailableSoTheProviderRetries()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<ProcessProviderEventCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "unavailable"));

        // Act
        var result = await Controller("{}").Receive("aliaddo", CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }
}
