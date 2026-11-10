using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using Enums = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Test.Services;

public class ElectronicInvoicingServiceTest
{
    private readonly Mock<IMediator> mediator = new();

    private readonly ElectronicInvoicingService service;

    private readonly ServerCallContext context = TestServerCallContext.Create("IssueDocument", null, DateTime.UtcNow.AddMinutes(1), new Metadata(), CancellationToken.None, "127.0.0.1", null, null, (_) => Task.CompletedTask, () => new WriteOptions(), (_) => { });

    public ElectronicInvoicingServiceTest()
    {
        service = new ElectronicInvoicingService(mediator.Object);
    }

    private static IssueDocumentRequest Request(Guid id) => new()
    {
        Id = id.ToString(),
        IssuerTenant = TestData.Tenant.ToString(),
        Type = DocumentType.Invoice,
        Reference = "licenses:order:1",
        Content = new DocumentContent
        {
            Buyer = new Buyer { PersonTypeCode = "1", IdentificationTypeCode = "31", IdentificationNumber = "900000001", CheckDigit = "7", Name = "Comprador de Prueba SAS", Email = "comprador@example.com" },
            Lines = { new Line { Code = "SVC-1", Description = "Servicio", Quantity = "2", UnitCode = "94", UnitPrice = 1_000_000, Amount = 2_000_000, Taxes = { new LineTax { Code = "01", Name = "IVA", RateBasisPoints = 1900, BaseAmount = 2_000_000, Amount = 380_000 } } } },
            Totals = new Totals { Currency = "COP", GrossAmount = 2_000_000, TaxAmount = 380_000, PayableAmount = 2_380_000 },
            PaymentMeans = new PaymentMeans { FormCode = "1", MethodCode = "10" },
            DueDate = "2026-11-30"
        }
    };

    [Fact]
    public async Task IssueDocument_Request_SendsTheCommandWithTheTenantAndTheContentInMinorUnits()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Draft, "SETP", 1));

        // Act
        var response = await service.IssueDocument(Request(id), context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<IssueElectronicDocumentCommand>(c => c.Id == id && c.Tenant == TestData.Tenant && c.Type == Enums.ElectronicDocumentType.Invoice && c.Content.Lines[0].Quantity == 2m && c.Content.Totals.PayableAmount == 2_380_000 && c.Content.DueDate == new NodaTime.LocalDate(2026, 11, 30)), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal((DocumentStatus.Draft, "SETP", 1L), (response.Status, response.Prefix, response.Consecutive));
    }

    [Fact]
    public async Task IssueDocument_IdReusedWithAnotherContent_AnswersAlreadyExists()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Application, Application.Errors.DocumentIdReusedWithOtherContent.GetCode(), "reused"));

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.IssueDocument(Request(Guid.NewGuid()), context));

        // Assert
        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);
    }

    [Fact]
    public async Task IssueDocument_ProviderNotAnswering_AnswersUnavailable()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, Infrastructure.Errors.VaultUnavailable.GetCode(), "vault"));

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.IssueDocument(Request(Guid.NewGuid()), context));

        // Assert
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
    }

    [Fact]
    public async Task IssueDocument_TypeNotSet_ThrowsInvalidDocumentType()
    {
        // Arrange
        var request = Request(Guid.NewGuid());

        request.Type = DocumentType.Unspecified;

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => service.IssueDocument(request, context));

        // Assert
        Assert.Equal(Infrastructure.Errors.InvalidDocumentType.GetCode(), exception.Code);
    }

    [Fact]
    public async Task IssueDocument_WithoutDueDate_SendsItWithoutOne()
    {
        // Arrange: a cash sale; proto3 has no absent string, so it comes empty.
        var id = Guid.NewGuid();
        var request = Request(id);

        request.Content.DueDate = string.Empty;
        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Draft, "SETP", 1));

        // Act
        await service.IssueDocument(request, context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<IssueElectronicDocumentCommand>(c => c.Id == id && c.Content.DueDate == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueDocument_DateThatIsNotACalendarDate_ThrowsInvalidDate()
    {
        // Arrange
        var request = Request(Guid.NewGuid());

        request.Content.DueDate = "2026-11-30T00:00:00Z";

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => service.IssueDocument(request, context));

        // Assert
        Assert.Equal(Infrastructure.Errors.InvalidDate.GetCode(), exception.Code);
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
    public async Task GetDocument_ValidatedDocument_CarriesItsNumberAndCufe()
    {
        // Arrange
        var document = new ElectronicDocumentDto { Id = Guid.NewGuid(), Tenant = TestData.Tenant, Reference = "licenses:order:1", Status = Enums.ElectronicDocumentStatus.Validated, Prefix = "SETP", Consecutive = 7, Cufe = "cufe-7", Content = TestData.InvoiceContent() };

        mediator.Setup(x => x.Send(It.Is<GetElectronicDocumentByIdQuery>(q => q.Id == document.Id && q.Tenant == TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var response = await service.GetDocument(new GetDocumentRequest { Id = document.Id.ToString(), Tenant = TestData.Tenant.ToString() }, context);

        // Assert
        Assert.Equal((DocumentStatus.Validated, "SETP", 7L, "cufe-7"), (response.Status, response.Prefix, response.Consecutive, response.Cufe));
    }

    [Fact]
    public async Task RetryDocument_WithoutContent_OnlyBringsItForward()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<RetryElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Sent, "SETP", 1));

        // Act
        await service.RetryDocument(new RetryDocumentRequest { Id = id.ToString(), Tenant = TestData.Tenant.ToString() }, context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<RetryElectronicDocumentCommand>(c => c.Id == id && c.Content == null && c.Tenant == TestData.Tenant), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetIssuer_TenantWithoutIssuer_SaysItIsNotRegistered()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<GetIssuerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerDto?)null);

        // Act
        var response = await service.GetIssuer(new GetIssuerRequest { Tenant = TestData.Tenant.ToString() }, context);

        // Assert
        Assert.Equal((false, false, 0L), (response.Registered, response.CanIssue, response.Quota.Remaining));
    }

    [Fact]
    public async Task GetIssuer_EnabledIssuer_CarriesItsQuotaAndCapabilities()
    {
        // Arrange
        var issuer = new IssuerDto
        {
            Id = Guid.NewGuid(),
            Status = Enums.IssuerStatus.Enabled,
            Provider = "Aliaddo",
            CanIssue = true,
            Quota = new QuotaDto { Limit = 100, Used = 1, Remaining = 99, PeriodEnd = TestData.Now },
            Capabilities = new CapabilitiesDto { DocumentTypes = [Enums.ElectronicDocumentType.Invoice, Enums.ElectronicDocumentType.PosTicket] }
        };

        mediator.Setup(x => x.Send(It.Is<GetIssuerQuery>(q => q.PlatformIssuer), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        // Act
        var response = await service.GetIssuer(new GetIssuerRequest { PlatformIssuer = true }, context);

        // Assert
        Assert.Equal((true, IssuerStatus.IssuerEnabled, 99L, 2), (response.CanIssue, response.Status, response.Quota.Remaining, response.Capabilities.DocumentTypes.Count));
    }

    [Fact]
    public async Task IssueDocument_WithoutTenant_IssuesAsThePlatformsIssuer()
    {
        // Arrange: ms-licenses invoices a license it sold; the buyer travels in the content, not as the issuer.
        var id = Guid.NewGuid();
        var request = Request(id);

        request.ClearIssuerTenant();

        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Draft, "EF", 1));

        // Act
        await service.IssueDocument(request, context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<IssueElectronicDocumentCommand>(c => c.Id == id && c.Tenant == null && c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueDocument_WithTenant_IssuesAsThatTenantAndNotThePlatform()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<IssueElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Draft, "SETP", 1));

        // Act
        await service.IssueDocument(Request(id), context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<IssueElectronicDocumentCommand>(c => c.Tenant == TestData.Tenant && !c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueDocument_TenantAndPlatformIssuer_AnswersInvalidArgument()
    {
        // Arrange: the application refuses the ambiguous scope; the contract answers it as a bad request.
        var request = Request(Guid.NewGuid());

        request.PlatformIssuer = true;

        mediator.Setup(x => x.Send(It.Is<IssueElectronicDocumentCommand>(c => c.Tenant == TestData.Tenant && c.PlatformIssuer), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Application, Application.Errors.PlatformIssuerWithTenant.GetCode(), "both"));

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.IssueDocument(request, context));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task GetDocument_WithoutTenant_ReadsThePlatformsDocumentAndSaysSo()
    {
        // Arrange
        var document = new ElectronicDocumentDto { Id = Guid.NewGuid(), Tenant = Domain.PlatformScope.Tenant, PlatformIssuer = true, Reference = "licenses:order:1", Status = Enums.ElectronicDocumentStatus.Validated, Content = TestData.InvoiceContent() };

        mediator.Setup(x => x.Send(It.Is<GetElectronicDocumentByIdQuery>(q => q.Id == document.Id && q.Tenant == null && q.PlatformIssuer), It.IsAny<CancellationToken>())).ReturnsAsync(document);

        // Act
        var response = await service.GetDocument(new GetDocumentRequest { Id = document.Id.ToString() }, context);

        // Assert
        Assert.Equal((true, string.Empty), (response.PlatformIssuer, response.Tenant));
    }

    [Fact]
    public async Task RetryDocument_WithoutTenant_RetriesThePlatformsDocument()
    {
        // Arrange
        var id = Guid.NewGuid();

        mediator.Setup(x => x.Send(It.IsAny<RetryElectronicDocumentCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IssuedDocumentDto(id, Enums.ElectronicDocumentStatus.Sent, "EF", 1));

        // Act
        await service.RetryDocument(new RetryDocumentRequest { Id = id.ToString() }, context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<RetryElectronicDocumentCommand>(c => c.Id == id && c.Tenant == null && c.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetIssuer_WithoutTenant_AsksForThePlatformsIssuer()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<GetIssuerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerDto?)null);

        // Act
        await service.GetIssuer(new GetIssuerRequest(), context);

        // Assert
        mediator.Verify(x => x.Send(It.Is<GetIssuerQuery>(q => q.Tenant == null && q.PlatformIssuer), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetIssuer_ReservedTenantNamed_AnswersInvalidArgument()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.IsAny<GetIssuerQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Application, Application.Errors.TenantIsReserved.GetCode(), "reserved"));

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetIssuer(new GetIssuerRequest { Tenant = Domain.PlatformScope.Tenant.ToString() }, context));

        // Assert
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
