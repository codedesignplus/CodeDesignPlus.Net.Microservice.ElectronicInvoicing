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
/// A test that talks to Aliaddo Nitro in <c>mode = Test</c>: it runs only where the token of the organization is in the
/// environment (<c>ALIADDO_TOKEN</c>), and is skipped elsewhere, so the CI stays green without it.
/// </summary>
public sealed class AliaddoSandboxFactAttribute : FactAttribute
{
    /// <summary>
    /// The variable with the token of the parent organization.
    /// </summary>
    public const string TokenVariable = "ALIADDO_TOKEN";

    public AliaddoSandboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable)))
        {
            Skip = $"{TokenVariable} is not set: Aliaddo is not reachable from here.";
        }
    }
}

/// <summary>
/// The adapter of Aliaddo against Nitro in <c>mode = Test</c>, whose documents have no legal effect.
/// </summary>
/// <remarks>
/// Nitro has no separate sandbox: the token belongs to a real organization and the environment is the <c>mode</c> of each
/// document. The numbering is the test range of the tax authority (prefix SETP, 990000000 to 995000000), so the numbers
/// are random inside it.
/// </remarks>
/// <param name="output">Where the reasons of an answer are written, to read what Nitro said.</param>
public class AliaddoSandboxTest(ITestOutputHelper output)
{
    private static readonly ProviderNumberingRange InvoiceRange = new("fc8eac422eba16e22ffd8c6f94b3f40a6e38162c", "SETP", "18760000001", 990000000, 995000000, new LocalDate(2019, 1, 19), new LocalDate(2030, 1, 19));

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

        return new AliaddoProvider(new AliaddoClient(new Clients(options.BaseUrl), NullLogger<AliaddoClient>.Instance), Microsoft.Extensions.Options.Options.Create(options), NullLogger<AliaddoProvider>.Instance);
    }

    private static ProviderIssuer Issuer() => ProviderIssuer.From(IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData(), AliaddoProvider.ProviderCode, TestData.User), new Dictionary<string, string>());

    // An invented buyer whose check digit is right: the tax authority rejects a wrong one.
    private static DocumentContent Content() => TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { CheckDigit = "2" } };

    private static ProviderDocument Invoice(DocumentContent? content = null, long? number = null) => new(Guid.NewGuid(), ElectronicDocumentType.Invoice, new DocumentNumber("SETP", number ?? Random.Shared.NextInt64(990000000, 995000001)), InvoiceRange, content ?? Content(), "key", LocalDate.FromDateTime(DateTime.UtcNow));

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

    private sealed class Clients(Uri baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(120) };
    }
}
