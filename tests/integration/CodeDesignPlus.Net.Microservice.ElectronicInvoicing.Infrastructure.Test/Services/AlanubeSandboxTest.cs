using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

/// <summary>
/// A test that talks to the sandbox of Alanube: it runs only where the token of the sandbox is in the environment
/// (<c>ALANUBE_CO_TOKEN</c>), and is skipped elsewhere, so the CI stays green without it.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    /// <summary>
    /// The variable with the token of the main company in the sandbox.
    /// </summary>
    public const string TokenVariable = "ALANUBE_CO_TOKEN";

    public SandboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable)))
        {
            Skip = $"{TokenVariable} is not set: the sandbox of Alanube is not reachable from here.";
        }
    }
}

/// <summary>
/// The adapter of Alanube against its sandbox, which goes to the enablement environment of the tax authority.
/// </summary>
/// <remarks>
/// The company of the tests is an associated company of the sandbox registered with the tax id of Alegra
/// (900559088-2), the one the sandbox accepts; it is enabled for invoices and for the point of sale. Every company of
/// the sandbox shares that tax id, so the numbers are random inside the test range, as Alanube recommends. Each run
/// issues real documents in the sandbox, which costs nothing.
/// </remarks>
public class AlanubeSandboxTest
{
    private const string CompanyId = "01M7176VAQHTJVZ83BMY9TSTT8";

    private const string SandboxTestSet = "a70562e0-631e-4ceb-aa65-36887b57dc17";

    private static readonly ProviderNumberingRange InvoiceRange = new("fc8eac422eba16e22ffd8c6f94b3f40a6e38162c", "SETP", "18760000001", 990000000, 995000000, new LocalDate(2019, 1, 19), new LocalDate(2030, 1, 19));

    private static readonly ProviderNumberingRange PosRange = new(null, "EPOS", "18760000001", 1, 1000000, new LocalDate(2019, 1, 19), new LocalDate(2030, 1, 19));

    private readonly AlanubeProvider provider;

    public AlanubeSandboxTest()
    {
        var options = new AlanubeOptions
        {
            BaseUrl = new Uri("https://sandbox-api.alegra.com/e-provider/col/v1/"),
            Master = new AlanubeMasterOptions { Token = Environment.GetEnvironmentVariable(SandboxFactAttribute.TokenVariable) },
            CreditPaymentForm = "2",
            TaxSchemeByRegime = new() { ["48"] = "01", ["49"] = "ZZ" },
            Documents = new()
            {
                ["Invoice"] = new AlanubeDocumentOptions { DocumentType = "01" },
                ["CreditNote"] = new AlanubeDocumentOptions { DocumentType = "91", CorrectedDocumentType = "01" },
                ["DebitNote"] = new AlanubeDocumentOptions { CorrectedDocumentType = "01" },
                ["PosTicket"] = new AlanubeDocumentOptions { DocumentType = "20" }
            },
            ErrorCodes = new AlanubeErrorCodesOptions
            {
                IssuerNotReady = ["AEP1002", "AEP6008", "AEP6012", "AEP15008", "AEP18013"],
                AlreadyReceived = ["AEP6009", "AEP6010", "AEP7009", "AEP7010", "AEP8009", "AEP8010"]
            }
        };

        provider = new AlanubeProvider(new AlanubeClient(new Clients(options.BaseUrl), NullLogger<AlanubeClient>.Instance), Microsoft.Extensions.Options.Options.Create(options), NullLogger<AlanubeProvider>.Instance);
    }

    private static ProviderIssuer Issuer()
    {
        var data = new IssuerFiscalData("CO", "31", "900559088-2", "Kappali Pruebas Copropiedad S.A.S", "1", "48", ["R-99-PN"], TestData.Address(), "pruebas@example.com", "3000000000", "America/Bogota");

        return ProviderIssuer.From(IssuerAggregate.Register(TestData.Tenant, data, AlanubeProvider.ProviderCode, TestData.User), new Dictionary<string, string>()) with { ProviderIssuerId = CompanyId };
    }

    // An invented buyer whose check digit is right: the tax authority rejects a wrong one (rule FAK24).
    private static Buyer Buyer() => TestData.Buyer() with { CheckDigit = "2", TaxResponsibilities = ["R-99-PN"] };

    private static DocumentContent Content() => TestData.InvoiceContent() with { Buyer = Buyer() };

    private static long InvoiceNumber() => Random.Shared.NextInt64(990000000, 995000001);

    private static ProviderDocument Invoice(DocumentContent? content = null, long? number = null, int revision = 0, Guid? id = null) => new(id ?? Guid.NewGuid(), ElectronicDocumentType.Invoice, new DocumentNumber("SETP", number ?? InvoiceNumber()), InvoiceRange, content ?? Content(), "key") { Revision = revision };

    private async Task<ProviderSubmission> AcceptedInvoiceAsync()
    {
        var submission = await provider.SubmitAsync(Issuer(), Invoice(), CancellationToken.None);

        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);

        return submission;
    }

    [SandboxFact]
    public async Task ValidateCredentialsAsync_TokenOfTheSandbox_IsAccepted()
    {
        // Act
        var validation = await provider.ValidateCredentialsAsync(Issuer(), CancellationToken.None);

        // Assert
        Assert.True(validation.Accepted);
    }

    [SandboxFact]
    public async Task SubmitAsync_Invoice_IsAcceptedWithItsCufe()
    {
        // Act
        var submission = await provider.SubmitAsync(Issuer(), Invoice(), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, true), (submission.State.Status, submission.State.Cufe?.Length > 0));
    }

    [SandboxFact]
    public async Task SubmitAsync_SameDocumentTwice_AnswersTheSameDocument()
    {
        // Arrange
        var document = Invoice();

        // Act
        var first = await provider.SubmitAsync(Issuer(), document, CancellationToken.None);
        var again = await provider.SubmitAsync(Issuer(), document, CancellationToken.None);

        // Assert
        Assert.Equal(first.ProviderDocumentId, again.ProviderDocumentId);
    }

    [SandboxFact]
    public async Task SubmitAsync_NumberAlreadyAcceptedSentAsANewRevision_AdoptsTheAcceptedDocument()
    {
        // Arrange
        var number = InvoiceNumber();
        var id = Guid.NewGuid();
        var first = await provider.SubmitAsync(Issuer(), Invoice(number: number, id: id), CancellationToken.None);

        // Act: another key with the same number gets AEP6009, and what Alanube has with the number is adopted.
        var again = await provider.SubmitAsync(Issuer(), Invoice(number: number, id: id, revision: 1), CancellationToken.None);

        // Assert
        Assert.Equal((first.ProviderDocumentId, ProviderDocumentStatus.Accepted), (again.ProviderDocumentId, again.State.Status));
    }

    [SandboxFact]
    public async Task SubmitAsync_InvoiceOnCreditWithAPrepayment_IsAccepted()
    {
        // Arrange
        var content = Content() with { PaymentMeans = new PaymentMeans("2", "10"), DueDate = LocalDate.FromDateTime(DateTime.UtcNow).PlusDays(30), Prepayments = [new Prepayment("ANT-1", 500_000, LocalDate.FromDateTime(DateTime.UtcNow).PlusDays(-1))], Totals = TestData.Totals() with { PrepaymentAmount = 500_000, PayableAmount = 1_880_000 } };

        // Act
        var submission = await provider.SubmitAsync(Issuer(), Invoice(content), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    [SandboxFact]
    public async Task SubmitAsync_InvoiceWithADiscountOnALine_IsAccepted()
    {
        // Arrange: 3 units of 3.333,33 with 1.000,00 off.
        var line = new DocumentLine("SVC-2", "Servicio con descuento", 3, "94", 333_333, 100_000, 899_999, [new LineTax("01", "IVA", 1900, 899_999, 171_000)], []);
        var content = Content() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 999_999, 100_000, 171_000, 0, 0, 1_070_999) };

        // Act
        var submission = await provider.SubmitAsync(Issuer(), Invoice(content), CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    [SandboxFact]
    public async Task GetFilesAsync_AcceptedInvoice_HandsOverTheXmlTheAttachedDocumentAndThePdf()
    {
        // Arrange
        var invoice = await AcceptedInvoiceAsync();

        // Act
        var files = await provider.GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, invoice.ProviderDocumentId, invoice.State.Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal((true, true, true), (files.Xml?.Content.Length > 0, files.AttachedDocument?.Content.Length > 0, files.Pdf?.Content.Take(4).SequenceEqual("%PDF"u8.ToArray())));
    }

    [SandboxFact]
    public async Task SubmitAsync_CreditNoteOnAnAcceptedInvoice_IsAccepted()
    {
        // Arrange
        var invoice = await AcceptedInvoiceAsync();
        var reference = new BillingReference(null, invoice.State.Number!.Prefix, invoice.State.Number.Consecutive, invoice.State.Cufe!, invoice.State.IssueDate!.Value);
        var note = new ProviderDocument(Guid.NewGuid(), ElectronicDocumentType.CreditNote, new DocumentNumber("NCKP", Random.Shared.NextInt64(1, 99_999_999)), null, Content() with { BillingReference = reference, CorrectionConceptCode = "2" }, "key");

        // Act
        var submission = await provider.SubmitAsync(Issuer(), note, CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    [SandboxFact]
    public async Task SubmitAsync_PosTicketAndItsAdjustmentNote_AreAccepted()
    {
        // Arrange: a ticket to the final consumer, as the gate charges a visitor who does not ask for an invoice.
        var consumer = Buyer() with { PersonTypeCode = "2", IdentificationTypeCode = "13", IdentificationNumber = "222222222222", CheckDigit = null, Name = "Consumidor final", TaxLiabilityCode = "ZZ" };
        var ticket = new ProviderDocument(Guid.NewGuid(), ElectronicDocumentType.PosTicket, new DocumentNumber("EPOS", Random.Shared.NextInt64(1, 1_000_001)), PosRange, Content() with { Buyer = consumer }, "key");

        // Act
        var issued = await provider.SubmitAsync(Issuer(), ticket, CancellationToken.None);
        var reference = new BillingReference(null, issued.State.Number!.Prefix, issued.State.Number.Consecutive, issued.State.Cufe!, issued.State.IssueDate!.Value);
        var adjustment = new ProviderDocument(Guid.NewGuid(), ElectronicDocumentType.PosAdjustmentNote, new DocumentNumber("NAKP", Random.Shared.NextInt64(1, 99_999_999)), null, Content() with { Buyer = consumer, BillingReference = reference, CorrectionConceptCode = "2" }, "key");
        var adjusted = await provider.SubmitAsync(Issuer(), adjustment, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, ProviderDocumentStatus.Accepted), (issued.State.Status, adjusted.State.Status));
    }

    [SandboxFact]
    public async Task GetStateAsync_ByItsNumber_FindsTheInvoice()
    {
        // Arrange
        var invoice = await AcceptedInvoiceAsync();

        // Act
        var state = await provider.GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, null, invoice.State.Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(invoice.ProviderDocumentId, state?.ProviderDocumentId);
    }

    [SandboxFact]
    public async Task RunTestSetAsync_TestSetOfTheSandbox_IsAccepted()
    {
        // Act
        var testSet = await provider.RunTestSetAsync(Issuer(), ElectronicDocumentType.Invoice, SandboxTestSet, CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, testSet.Status);
    }

    private sealed class Clients(Uri baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = name == AlanubeClient.FilesClientName ? null : baseUrl, Timeout = TimeSpan.FromSeconds(120) };
    }
}
