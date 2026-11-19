using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Xunit.Abstractions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

/// <summary>
/// A test that talks to Aliaddo Nitro in <c>mode = Test</c>: it runs only where it is asked for
/// (<c>RUN_PROVIDER_SANDBOX_TESTS=1</c>) and the token of the organization is in the environment (<c>ALIADDO_TOKEN</c>),
/// and is skipped elsewhere.
/// </summary>
public sealed class AliaddoSandboxFactAttribute : FactAttribute
{
    /// <summary>
    /// The variable with the token of the parent organization.
    /// </summary>
    public const string TokenVariable = "ALIADDO_TOKEN";

    public AliaddoSandboxFactAttribute()
    {
        Skip = ProviderSandbox.SkipReason(TokenVariable);
    }
}

/// <summary>
/// The same as <see cref="AliaddoSandboxFactAttribute"/>, for a test with several cases.
/// </summary>
public sealed class AliaddoSandboxTheoryAttribute : TheoryAttribute
{
    public AliaddoSandboxTheoryAttribute()
    {
        Skip = ProviderSandbox.SkipReason(AliaddoSandboxFactAttribute.TokenVariable);
    }
}

/// <summary>
/// The adapter of Aliaddo against Nitro in <c>mode = Test</c>, whose documents have no legal effect.
/// </summary>
/// <remarks>
/// Nitro has no separate sandbox: the token belongs to a real organization and the environment is the <c>mode</c> of each
/// document. A document in <c>mode = Test</c> reaches the enablement environment of the tax authority and <b>spends a
/// document of the account</b>, also when it is rejected: each test here is run by its name, on purpose. The numbering
/// is the range of the test set of the tax authority (prefix SETT, 1 to 5.000.000), so the numbers are random inside it.
/// </remarks>
/// <param name="output">Where the reasons of an answer are written, to read what Nitro said.</param>
public class AliaddoSandboxTest(ITestOutputHelper output)
{
    private static readonly ProviderNumberingRange InvoiceRange = new("fc8eac422eba16e22ffd8c6f94b3f40a6e38162c", "SETT", "18760000001", 1, 5000000, new LocalDate(2019, 1, 19), new LocalDate(2030, 1, 19));

    private readonly AliaddoProvider provider = Provider();

    private static AliaddoProvider Provider()
    {
        var options = new AliaddoOptions
        {
            BaseUrl = new Uri("https://nitro.aliaddo.net"),
            Master = new AliaddoMasterOptions { Token = Environment.GetEnvironmentVariable(AliaddoSandboxFactAttribute.TokenVariable) },
            LineDiscountName = "Descuento",
            Documents = new()
            {
                ["Invoice"] = new AliaddoDocumentOptions { Code = "01", OperationType = "10", Format = "Standard" },
                ["CreditNote"] = new AliaddoDocumentOptions { Code = "91", OperationType = "20", Format = "Standard" },
                ["DebitNote"] = new AliaddoDocumentOptions { Code = "92", OperationType = "30", Format = "Standard" },
                ["PosTicket"] = new AliaddoDocumentOptions { Code = "20", OperationType = "10", Format = "PosX80mm" }
            }
        };

        return new AliaddoProvider(new AliaddoClient(new Clients(options.BaseUrl), NullLogger<AliaddoClient>.Instance), Microsoft.Extensions.Options.Options.Create(options), SystemClock.Instance, NullLogger<AliaddoProvider>.Instance);
    }

    private static ProviderIssuer Issuer() => ProviderIssuer.From(IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData(), AliaddoProvider.ProviderCode, TestData.User), new Dictionary<string, string>());

    // An invented buyer whose check digit is right: the tax authority rejects a wrong one.
    private static DocumentContent Content() => TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { CheckDigit = "2" } };

    private static ProviderDocument Invoice(DocumentContent? content = null, long? number = null) => new(Guid.NewGuid(), ElectronicDocumentType.Invoice, new DocumentNumber("SETT", number ?? Random.Shared.NextInt64(1000, 5000001)), InvoiceRange, content ?? Content(), "key", LocalDate.FromDateTime(DateTime.UtcNow));

    private void Write(ProviderSubmission submission) => output.WriteLine($"{submission.ProviderDocumentId} {submission.State.Status} {submission.State.Raw} {submission.State.Cufe} {string.Join(" | ", submission.State.Reasons)}");

    [AliaddoSandboxFact]
    public async Task SubmitAsync_InvoiceInTestMode_IsAnswered()
    {
        // Act
        var submission = await provider.SubmitAsync(Issuer(), Invoice(), CancellationToken.None);

        // Assert
        Write(submission);
        Assert.NotEqual(ProviderDocumentStatus.Rejected, submission.State.Status);
    }

    [AliaddoSandboxFact]
    public async Task SubmitAsync_LineOfSeveralUnitsWhoseTaxDoesNotDivide_IsValidated()
    {
        // Arrange: 3 units of 333,00 with 19 % VAT, rounded once for the line to 190,00 (189,81 exact), as the caller does.
        var line = TestData.Line() with { Quantity = 3, UnitPrice = 33_300, Amount = 99_900, Taxes = [new LineTax("01", "IVA", 1900, 99_900, 19_000)] };
        var content = Content() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 99_900, 0, 19_000, 0, 0, 118_900) };

        // Act
        var submission = await provider.SubmitAsync(Issuer(), Invoice(content), CancellationToken.None);

        // Assert
        Write(submission);
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    // The invoice the tax authority validated on 19-nov-2026 in mode = Test: one unit of 10.000 with 1.900 of VAT.
    private static readonly BillingReference ValidatedInvoice = new(null, "SETT", 2416024, "e074ca16dcb944645ea806f713839b7899de92dac1c410e4d3c3c19fd8e86ed00bae6cce3f7f530207572dd30b957434", new LocalDate(2026, 11, 19));

    private static DocumentContent NoteOf(string concept)
    {
        var line = TestData.Line() with { Quantity = 1, Amount = 1_000_000, Taxes = [new LineTax("01", "IVA", 1900, 1_000_000, 190_000)] };

        return Content() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 1_000_000, 0, 190_000, 0, 0, 1_190_000), BillingReference = ValidatedInvoice, CorrectionConceptCode = concept };
    }

    [AliaddoSandboxTheory]
    [InlineData(ElectronicDocumentType.CreditNote, "NC", "2")]
    [InlineData(ElectronicDocumentType.DebitNote, "ND", "1")]
    public async Task SubmitAsync_NoteWithItsOwnPrefixAndTheResolutionOfItsInvoice_IsValidated(ElectronicDocumentType type, string prefix, string concept)
    {
        // Arrange: Nitro asks for a resolution also in a note; it goes with the one of the invoice it corrects.
        var note = new ProviderDocument(Guid.NewGuid(), type, new DocumentNumber(prefix, Random.Shared.NextInt64(1000, 5000001)), InvoiceRange, NoteOf(concept), "key", LocalDate.FromDateTime(DateTime.UtcNow.AddHours(-5)));

        // Act
        var submission = await provider.SubmitAsync(Issuer(), note, CancellationToken.None);

        // Assert
        Write(submission);

        var state = await provider.GetStateAsync(Issuer(), new ProviderDocumentLookup(type, submission.ProviderDocumentId, note.Number, "key"), CancellationToken.None);

        output.WriteLine($"issued as {state?.Number}");
        Assert.Equal((ProviderDocumentStatus.Accepted, note.Number), (submission.State.Status, state?.Number));
    }

    [AliaddoSandboxFact]
    public async Task GetFilesAsync_ValidatedInvoice_BringsItsPdfAndItsXml()
    {
        // Act: only reads, it spends nothing.
        var files = await provider.GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, null, new DocumentNumber(ValidatedInvoice.Prefix, ValidatedInvoice.Consecutive), "key"), CancellationToken.None);

        // Assert
        output.WriteLine($"{files.Pdf?.Name} {files.Pdf?.Content.Length} | {files.Xml?.Name} {files.Xml?.Content.Length} | {files.AttachedDocument?.Name} {files.AttachedDocument?.Content.Length}");
        Assert.Equal(("%PDF", true, true), (System.Text.Encoding.ASCII.GetString(files.Pdf!.Content, 0, 4), System.Text.Encoding.UTF8.GetString(files.Xml!.Content).Contains("<Invoice"), System.Text.Encoding.UTF8.GetString(files.AttachedDocument!.Content).Contains("AttachedDocument")));
    }

    private sealed class Clients(Uri baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(120) };
    }
}
