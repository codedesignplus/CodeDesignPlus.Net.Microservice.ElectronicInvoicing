using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using Currency = CodeDesignPlus.Net.ValueObjects.Financial.Currency;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Common;

public class NumberingServiceTest
{
    private static readonly LocalDate Today = new(2026, 11, 4);

    private readonly Mock<INumberingRangeRepository> ranges = new();

    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();

    private async Task<string> CodeOfSelectAsync(params NumberingRangeAggregate[] found)
    {
        ranges.Setup(x => x.FindByTypeAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<CancellationToken>())).ReturnsAsync([.. found]);

        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => new NumberingService(ranges.Object).SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, Today, CancellationToken.None));

        return exception.Code;
    }

    [Fact]
    public async Task SelectAsync_ValidRangeWithNumbers_ReturnsIt()
    {
        // Arrange
        var range = TestData.Range(issuer);

        ranges.Setup(x => x.FindByTypeAsync(TestData.Tenant, ElectronicDocumentType.Invoice, It.IsAny<CancellationToken>())).ReturnsAsync([range]);

        // Act
        var selected = await new NumberingService(ranges.Object).SelectAsync(TestData.Tenant, ElectronicDocumentType.Invoice, Today, CancellationToken.None);

        // Assert
        Assert.Same(range, selected);
    }

    [Fact]
    public async Task SelectAsync_OnlyAnExhaustedRange_ThrowsNumberingRangeExhausted()
    {
        // Arrange
        var range = TestData.Range(issuer, from: 1, to: 1);

        range.Reserve(Today);

        // Act & Assert
        Assert.Equal(DomainErrors.NumberingRangeExhausted.GetCode(), await CodeOfSelectAsync(range));
    }

    [Fact]
    public async Task SelectAsync_OnlyAnExpiredRange_ThrowsNumberingRangeExpired()
    {
        // Arrange
        var range = NumberingRangeAggregate.Create(TestData.Tenant, issuer.Id, TestData.Resolution() with { ValidFrom = new LocalDate(2024, 1, 1), ValidUntil = new LocalDate(2025, 1, 1) }, NumberingSource.Platform, TestData.User);

        // Act & Assert
        Assert.Equal(DomainErrors.NumberingRangeExpired.GetCode(), await CodeOfSelectAsync(range));
    }

    [Fact]
    public async Task SelectAsync_NoRange_ThrowsNoUsableNumberingRange()
    {
        // Act & Assert
        Assert.Equal(AppErrors.NoUsableNumberingRange.GetCode(), await CodeOfSelectAsync());
    }
}

public class QuotaGateTest
{
    private readonly Mock<IDocumentQuotaRepository> repository = new();

    private readonly ElectronicInvoicingOptions settings = Fakes.Options();

    public QuotaGateTest()
    {
        repository.EmulateOptimisticUpdate<IDocumentQuotaRepository, DocumentQuotaAggregate>();
    }

    [Fact]
    public async Task ReserveAsync_PlatformIssuerWithoutAllowance_UsesTheReservationOfTheConfiguration()
    {
        // Arrange: the platform's issuer never received a license allowance.
        DocumentQuotaAggregate? stored = null;

        repository.Setup(x => x.FindAsync<DocumentQuotaAggregate>(DocumentQuotaAggregate.IdFor(TestData.PlatformTenant), It.IsAny<CancellationToken>())).ReturnsAsync(() => stored!);
        repository.Setup(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<DocumentQuotaAggregate>>>(), It.IsAny<Func<DocumentQuotaAggregate, bool>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<DocumentQuotaAggregate>>, Func<DocumentQuotaAggregate, bool>, CancellationToken>(async (load, change, _) =>
            {
                var quota = await load();

                stored = quota;

                return change(quota);
            });

        // Act
        await new QuotaGate(repository.Object, Fakes.Options(settings)).ReserveAsync(TestData.PlatformTenant, ElectronicDocumentType.Invoice, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Equal((500L, 1L), (stored!.Limit, stored.Used));
    }

    [Fact]
    public async Task ReserveAsync_TenantWithoutAllowance_ThrowsQuotaExhaustedForAnInvoice()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => new QuotaGate(repository.Object, Fakes.Options(settings)).ReserveAsync(TestData.Tenant, ElectronicDocumentType.Invoice, TestData.Now, CancellationToken.None));

        // Assert
        Assert.Equal(DomainErrors.QuotaExhausted.GetCode(), exception.Code);
    }

    [Fact]
    public async Task ReserveAsync_TenantWithoutAllowance_LetsACreditNoteGo()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => new QuotaGate(repository.Object, Fakes.Options(settings)).ReserveAsync(TestData.Tenant, ElectronicDocumentType.CreditNote, TestData.Now, CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }
}

public class ApplyLicenseQuotaCommandHandlerTest
{
    private readonly Mock<IDocumentQuotaRepository> repository = new();

    private readonly Mock<IPubSub> pubsub = new();

    [Theory]
    [InlineData("1200", 1200)]
    [InlineData("not-a-number", 0)]
    [InlineData("-5", 0)]
    public void LimitOf_ConfiguredKey_IsItsIntegerValueOrZero(string value, long expected)
    {
        // Act
        var limit = ApplyLicenseQuotaCommandHandler.LimitOf(new Dictionary<string, string> { ["documents_key"] = value, ["other"] = "999" }, ["documents_key"]);

        // Assert
        Assert.Equal(expected, limit);
    }

    [Fact]
    public void LimitOf_SeveralConfiguredKeys_AddsThem()
    {
        // Act
        var limit = ApplyLicenseQuotaCommandHandler.LimitOf(new Dictionary<string, string> { ["a"] = "100", ["b"] = "20" }, ["a", "b", "missing"]);

        // Assert
        Assert.Equal(120, limit);
    }

    [Fact]
    public async Task Handle_FirstAllowance_CreatesTheQuotaUntilTheEndOfTheLicense()
    {
        // Arrange
        DocumentQuotaAggregate? stored = null;
        var end = TestData.Now + Duration.FromDays(365);

        repository.Setup(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<DocumentQuotaAggregate>>>(), It.IsAny<Func<DocumentQuotaAggregate, bool>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<DocumentQuotaAggregate>>, Func<DocumentQuotaAggregate, bool>, CancellationToken>(async (load, change, _) =>
            {
                stored = await load();

                return change(stored);
            });

        var handler = new ApplyLicenseQuotaCommandHandler(repository.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));

        // Act
        await handler.Handle(new ApplyLicenseQuotaCommand(TestData.Tenant, new Dictionary<string, string> { ["documents_key"] = "600" }, end, TestData.Now), CancellationToken.None);

        // Assert
        Assert.Equal((600L, (Instant?)end), (stored!.Limit, stored.PeriodEnd));
    }

    [Fact]
    public async Task Handle_PlatformTenant_IgnoresItsLicense()
    {
        // Arrange
        var handler = new ApplyLicenseQuotaCommandHandler(repository.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));

        // Act
        var changed = await handler.Handle(new ApplyLicenseQuotaCommand(TestData.PlatformTenant, new Dictionary<string, string> { ["documents_key"] = "600" }, TestData.Now + Duration.FromDays(365), TestData.Now), CancellationToken.None);

        // Assert
        Assert.False(changed);
    }
}

public class DocumentContentResolverTest
{
    private readonly Mock<ICurrencyGrpc> currencies = new();

    private readonly Mock<IElectronicDocumentRepository> documents = new();

    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();

    private readonly DocumentContentResolver resolver;

    public DocumentContentResolverTest()
    {
        currencies.Setup(x => x.GetCurrencyAsync(It.IsAny<Guid?>(), "COP", It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Currency.Create(Guid.NewGuid(), "Peso colombiano", "COP", "$", 2, 170));

        resolver = new DocumentContentResolver(currencies.Object, documents.Object);
    }

    private static DocumentContentInput Input(BillingReferenceInput? reference = null) => new(TestData.Buyer(), [TestData.Line()], new DocumentTotalsInput("COP", 2_000_000, 0, 380_000, 0, 0, 2_380_000), [], new PaymentMeans("1", "10"), new LocalDate(2026, 11, 30), [], reference, reference is null ? null : "2");

    [Fact]
    public async Task ResolveAsync_Invoice_TakesTheDecimalDigitsOfTheCurrency()
    {
        // Act
        var content = await resolver.ResolveAsync(issuer, ElectronicDocumentType.Invoice, Input(), Fakes.Capabilities(), CancellationToken.None);

        // Assert
        Assert.Equal((short)2, content.Totals.CurrencyDecimalDigits);
    }

    [Fact]
    public async Task ResolveAsync_NoteOnADocumentIssuedHere_TakesItsNumberCufeAndDate()
    {
        // Arrange
        var invoice = TestData.Document();

        invoice.AssignNumber(new DocumentNumber("SETP", 990000005), Guid.NewGuid());
        invoice.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));
        invoice.Validate("provider-1", "cufe-of-the-invoice", null, new LocalDate(2026, 11, 3), "Aprobada", null, TestData.Now);
        documents.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(invoice.Id, TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        // Act
        var content = await resolver.ResolveAsync(issuer, ElectronicDocumentType.CreditNote, Input(new BillingReferenceInput(invoice.Id, null, null, null, null)), Fakes.Capabilities(), CancellationToken.None);

        // Assert
        Assert.Equal(new BillingReference(invoice.Id, "SETP", 990000005, "cufe-of-the-invoice", new LocalDate(2026, 11, 3)), content.BillingReference);
    }

    [Fact]
    public async Task ResolveAsync_NoteOnADocumentNotValidated_ThrowsBillingReferenceNotFound()
    {
        // Arrange
        var invoice = TestData.Document();

        documents.Setup(x => x.FindAsync<ElectronicDocumentAggregate>(invoice.Id, TestData.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => resolver.ResolveAsync(issuer, ElectronicDocumentType.CreditNote, Input(new BillingReferenceInput(invoice.Id, null, null, null, null)), Fakes.Capabilities(), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.BillingReferenceNotFound.GetCode(), exception.Code);
    }

    [Fact]
    public async Task ResolveAsync_NoteOnAnExternalDocumentWithAProviderThatCannot_ThrowsExternalBillingReferenceNotSupported()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => resolver.ResolveAsync(issuer, ElectronicDocumentType.CreditNote, Input(new BillingReferenceInput(null, "FE", 1, "cufe", new LocalDate(2026, 1, 1))), Fakes.Capabilities(externalReference: false), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.ExternalBillingReferenceNotSupported.GetCode(), exception.Code);
    }
}

public class IdempotencyKeysTest
{
    [Fact]
    public void For_ADocument_IsAlwaysTheSameAndFitsInThirtyCharacters()
    {
        // Arrange
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        // Act
        var key = IdempotencyKeys.For(id);

        // Assert
        Assert.Equal((25, key, true), (key.Length, IdempotencyKeys.For(id), key.All(char.IsLetterOrDigit)));
    }

    [Fact]
    public void For_TwoDocuments_AreDifferent()
    {
        // Act & Assert
        Assert.NotEqual(IdempotencyKeys.For(Guid.NewGuid()), IdempotencyKeys.For(Guid.NewGuid()));
    }
}

public class ElectronicInvoicingOptionsTest
{
    [Fact]
    public void Validate_ShippedShape_HasNoProblems()
    {
        // Act
        var problems = Fakes.Options().Validate(new System.ComponentModel.DataAnnotations.ValidationContext(new object())).ToList();

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void Validate_WithoutSystemUser_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.SystemUser = Guid.Empty;

        // Act
        var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("SystemUser"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(10, 30)]
    public void DelayAfter_Attempts_GrowsExponentiallyUpToTheCeiling(int attempts, int minutes)
    {
        // Act
        var delay = Fakes.Options().StatusCheck.DelayAfter(attempts);

        // Assert
        Assert.Equal(Duration.FromMinutes(minutes), delay);
    }

    [Fact]
    public void ProvidersFor_ACountry_PutsTheDefaultFirst()
    {
        // Act & Assert
        Assert.Equal(["Simulated", "Other"], Fakes.Options().ProvidersFor("co"));
    }
}
