using CodeDesignPlus.Net.Core.Abstractions.Models.Pager;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetAllElectronicDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using Newtonsoft.Json;
using C = CodeDesignPlus.Net.Core.Abstractions.Models.Criteria;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Test.Controllers;

/// <summary>
/// The controllers fix the scope of every request: the platform's endpoints always work with the platform's issuer and
/// the tenant's endpoints never do, whatever the body says.
/// </summary>
public class PlatformControllersTest
{
    private readonly Mock<IMediator> mediator = new();

    [Fact]
    public async Task PlatformIssuerGetIssuer_Call_AsksForThePlatformsIssuer()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<GetIssuerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerDto?)null);

        // Act
        await new PlatformIssuerController(mediator.Object).GetIssuer(CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<GetIssuerQuery>(q => q.Tenant == null && q.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlatformIssuerStartTesting_BodyWithoutScope_SendsItForThePlatform()
    {
        // Act
        await new PlatformIssuerController(mediator.Object).StartTesting(new StartIssuerTestingCommand("test-set"), CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<StartIssuerTestingCommand>(c => c.TestSetId == "test-set" && c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssuerEnable_CommandMarkedForThePlatform_SendsItForTheTenantOfTheSession()
    {
        // Arrange: a tenant's endpoint cannot be turned into the platform's by what arrives.
        var command = new EnableIssuerCommand(IssuerEnvironment.Production) { PlatformIssuer = true };

        // Act
        await new IssuerController(mediator.Object).Enable(command, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<EnableIssuerCommand>(c => !c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlatformIssuerSuspend_Call_SuspendsThePlatformsIssuer()
    {
        // Act
        await new PlatformIssuerController(mediator.Object).Suspend(CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<SuspendIssuerCommand>(c => c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlatformElectronicDocumentGetAll_Call_ReadsThePlatformsDocuments()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<GetAllElectronicDocumentsQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Pagination<ElectronicDocumentDto>([], 0, 10, 0));

        // Act
        await new PlatformElectronicDocumentController(mediator.Object).GetElectronicDocuments(new C.Criteria(), CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<GetAllElectronicDocumentsQuery>(q => q.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlatformElectronicDocumentRetry_Call_RetriesThePlatformsDocument()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<RetryElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, ElectronicDocumentStatus.Sent, null, null));

        // Act
        await new PlatformElectronicDocumentController(mediator.Object).RetryElectronicDocument(id, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<RetryElectronicDocumentCommand>(c => c.Id == id && c.Tenant == null && c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlatformNumberingRangeSync_Call_SyncsTheRangeOfThePlatformsIssuer()
    {
        // Act
        await new PlatformNumberingRangeController(mediator.Object).SyncNumberingRange(new SyncNumberingRangeCommand(ElectronicDocumentType.Invoice, "EF"), CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<SyncNumberingRangeCommand>(c => c.Prefix == "EF" && c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BodyOfACommand_WithPlatformIssuer_DoesNotBindIt()
    {
        // Act: the REST host reads the bodies with Newtonsoft.
        var command = JsonConvert.DeserializeObject<StartIssuerTestingCommand>("""{"testSetId":"test-set","platformIssuer":true}""")!;

        // Assert
        Assert.False(command.PlatformIssuer);
    }
}
