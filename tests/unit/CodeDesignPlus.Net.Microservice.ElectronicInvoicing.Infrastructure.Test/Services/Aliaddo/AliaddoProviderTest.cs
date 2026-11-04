using System.Security.Cryptography;
using System.Text;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;
using InfraErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Aliaddo;

public class AliaddoProviderTest
{
    private const string Approved = """{"Id":"21158a97-2b72-4db6-8b75-4aeeefa016c6","Cufe":"cufe-invented","Sequence":"SETP990000000","Qr":"qr","Status":"Aprobada","StatusReasons":["Procesado Correctamente."]}""";

    private readonly FakeAliaddo nitro = new();

    private static readonly DocumentNumber Number = new("SETP", 990000000);

    private static ProviderIssuer Issuer(IssuerEnvironment environment = IssuerEnvironment.Test, Dictionary<string, string>? secrets = null)
    {
        var issuer = TestData.EnabledIssuer();

        return ProviderIssuer.From(issuer, secrets ?? []) with { Environment = environment, TestSetId = environment == IssuerEnvironment.Habilitation ? "0e3d0669-03ed-4f70-a237-c9541d22c5a1" : null };
    }

    private static ProviderNumberingRange Range(string number = "18760000001") => new("technical-key-of-test", "SETP", number, 990000000, 995000000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    private static ProviderDocument Document(ElectronicDocumentType type = ElectronicDocumentType.Invoice, DocumentContent? content = null, ProviderNumberingRange? range = null) => new(Guid.NewGuid(), type, Number, range ?? Range(), content ?? (DocumentContent.IsNote(type) ? TestData.NoteContent() : TestData.InvoiceContent()), "key");

    private async Task<ProviderSubmission> SubmitAsync(ProviderDocument? document = null, ProviderIssuer? issuer = null) => await nitro.Provider().SubmitAsync(issuer ?? Issuer(), document ?? Document(), CancellationToken.None);

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice, "/v2/documents/invoices")]
    [InlineData(ElectronicDocumentType.CreditNote, "/v2/documents/invoices/credit-notes")]
    [InlineData(ElectronicDocumentType.DebitNote, "/v2/documents/invoices/debit-notes")]
    [InlineData(ElectronicDocumentType.PosTicket, "/v2/documents/equivalent-documents")]
    public async Task SubmitAsync_EachType_PostsToItsPathWithTheMasterToken(ElectronicDocumentType type, string path)
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(type));

        // Assert
        Assert.Equal((HttpMethod.Post, path, "Bearer invented-master-token"), (nitro.Requests.Single().Method, nitro.Requests.Single().Path, nitro.Requests.Single().Authorization));
    }

    [Fact]
    public async Task SubmitAsync_Invoice_SendsTheCodesTheNumberAndTheResolution()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync();

        // Assert
        var body = nitro.LastBody;

        Assert.Equal(("01", "10", "Test", "SETP", 990000000L, 18760000001L, "technical-key-of-test", "2026-11-30"), (body.GetProperty("code").GetString(), body.GetProperty("typeOperation").GetString(), body.GetProperty("mode").GetString(), body.GetProperty("prefix").GetString(), body.GetProperty("consecutive").GetInt64(), body.GetProperty("resolution").GetProperty("number").GetInt64(), body.GetProperty("resolution").GetProperty("key").GetString(), body.GetProperty("dueAt").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_Invoice_SendsTheIntegerAmountsInWholeUnitsAndTheRatesAsPercentages()
    {
        // Arrange: 2 units of 10.000,00 with 19 % VAT, in minor units of a currency with two decimals.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync();

        // Assert
        var line = nitro.LastBody.GetProperty("lines")[0];
        var tax = line.GetProperty("taxes")[0];
        var totals = nitro.LastBody.GetProperty("totals");

        Assert.Equal((10000L, 2L, 19m, 3800m, 20000m, 20000L, 3800L), (line.GetProperty("price").GetInt64(), line.GetProperty("quantity").GetInt64(), tax.GetProperty("rate").GetDecimal(), tax.GetProperty("amount").GetDecimal(), tax.GetProperty("base").GetDecimal(), totals.GetProperty("amount").GetInt64(), totals.GetProperty("taxesAmount").GetInt64()));
    }

    [Fact]
    public async Task SubmitAsync_InHabilitation_SendsTheTestSet()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(issuer: Issuer(IssuerEnvironment.Habilitation));

        // Assert
        Assert.Equal(("Habilitation", "0e3d0669-03ed-4f70-a237-c9541d22c5a1"), (nitro.LastBody.GetProperty("mode").GetString(), nitro.LastBody.GetProperty("testSetId").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_OutsideHabilitation_DoesNotSendATestSet()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync();

        // Assert
        Assert.False(nitro.LastBody.TryGetProperty("testSetId", out _));
    }

    [Fact]
    public async Task SubmitAsync_CreditNote_RefersToTheInvoiceItCorrects()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.CreditNote));

        // Assert
        var body = nitro.LastBody;

        Assert.Equal(("2", "SETP", 990000001L, "cufe-of-the-corrected-invoice", "2026-11-01", "SETP990000001"), (body.GetProperty("conceptCode").GetString(), body.GetProperty("invoicePrefix").GetString(), body.GetProperty("invoiceConsecutive").GetInt64(), body.GetProperty("invoiceCuv").GetString(), body.GetProperty("invoiceIssuedAt").GetString(), body.GetProperty("invoiceSequence").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_Approved_IsAcceptedWithItsCufe()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, "cufe-invented", "21158a97-2b72-4db6-8b75-4aeeefa016c6"), (submission.State.Status, submission.State.Cufe, submission.ProviderDocumentId));
    }

    [Fact]
    public async Task SubmitAsync_RejectedByTheTaxAuthority_IsRejectedWithItsReasons()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"x","Status":"Rechazada","StatusReasons":["Regla FAD06 rechazo."]}""");

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Dian, "Regla FAD06 rechazo."), (submission.State.Status, submission.State.RejectionOrigin, submission.State.Reasons.Single()));
    }

    [Fact]
    public async Task SubmitAsync_ValidationRefusal_IsRejectedByTheProviderAndNotRetried()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.UnprocessableEntity, """{"message":"El campo customer.identification es requerido."}""");

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Provider, "El campo customer.identification es requerido.", 1), (submission.State.Status, submission.State.RejectionOrigin, submission.State.Reasons.Single(), nitro.Requests.Count));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)520)]
    public async Task SubmitAsync_NitroFails_ThrowsProviderUnavailable(HttpStatusCode status)
    {
        // Arrange
        nitro.Answer = _ => (status, "");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SubmitAsync_TokenRejected_ThrowsProviderCredentialsRejected(HttpStatusCode status)
    {
        // Arrange
        nitro.Answer = _ => (status, "");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderCredentialsRejected.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_FractionalQuantity_IsRefusedWithoutCallingNitro()
    {
        // Arrange: 1,5 units of 10.000,00.
        var line = TestData.Line() with { Quantity = 1.5m, Amount = 1_500_000, Taxes = [new LineTax("01", "IVA", 1900, 1_500_000, 285_000)] };
        var content = TestData.InvoiceContent() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 1_500_000, 0, 285_000, 0, 0, 1_785_000) };

        // Act
        var submission = await SubmitAsync(Document(content: content));

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, 0), (submission.State.Status, nitro.Requests.Count));
    }

    [Fact]
    public async Task SubmitAsync_ResolutionNumberWithLetters_IsRefusedWithoutCallingNitro()
    {
        // Act
        var submission = await SubmitAsync(Document(range: Range("RES-1")));

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, 0), (submission.State.Status, nitro.Requests.Count));
    }

    [Fact]
    public async Task SubmitAsync_IssuerWithItsOwnToken_UsesIt()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(issuer: Issuer(secrets: new() { [AliaddoProvider.TokenSecret] = "invented-child-token" }));

        // Assert
        Assert.Equal("Bearer invented-child-token", nitro.Requests.Single().Authorization);
    }

    [Fact]
    public async Task SubmitAsync_WithoutMasterToken_ThrowsPlatformTokenNotConfigured()
    {
        // Arrange
        var options = FakeAliaddo.Options();

        options.Master.Token = "";

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => nitro.Provider(options).SubmitAsync(Issuer(), Document(), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.PlatformTokenNotConfigured.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData("Pendiente", ProviderDocumentStatus.Processing)]
    [InlineData("Procesando", ProviderDocumentStatus.Processing)]
    [InlineData("NoDisponible", ProviderDocumentStatus.DianUnavailable)]
    [InlineData("DianNoDisponible", ProviderDocumentStatus.DianUnavailable)]
    [InlineData("Aprobada", ProviderDocumentStatus.Accepted)]
    [InlineData("Rechazada", ProviderDocumentStatus.Rejected)]
    [InlineData("Error", ProviderDocumentStatus.Processing)]
    [InlineData("SomethingNew", ProviderDocumentStatus.Processing)]
    public void StatusOf_StatusOfNitro_IsNormalized(string status, ProviderDocumentStatus expected)
    {
        // Act & Assert
        Assert.Equal(expected, AliaddoProvider.StatusOf(status));
    }

    [Fact]
    public async Task GetStateAsync_ById_ReadsTheCufeTheNumberAndTheIssueDate()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"doc-1","Prefix":"SETP","Consecutive":990000000,"Cuv":"cuv-1","IssuedAt":"2026-11-04T00:00:00","Status":"Aprobada"}""");

        // Act
        var state = await nitro.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "doc-1", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(("/v2/documents/invoices/doc-1", ProviderDocumentStatus.Accepted, "cuv-1", Number, (LocalDate?)new LocalDate(2026, 11, 4)), (nitro.Requests.Single().Path, state!.Status, state.Cufe, state.Number, state.IssueDate));
    }

    [Fact]
    public async Task GetStateAsync_WithoutId_FindsItByPrefixAndConsecutive()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"doc-1","Prefix":"SETP","Consecutive":990000000,"Status":"Procesando"}""");

        // Act
        await nitro.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.CreditNote, null, Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal("/v2/documents/invoices/credit-notes/find-by-prefix-and-consecutive?prefix=SETP&consecutive=990000000", nitro.Requests.Single().Path);
    }

    [Fact]
    public async Task GetStateAsync_NotInNitro_ReturnsNull()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.NotFound, "");

        // Act
        var state = await nitro.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "doc-1", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetStateAsync_PosTicket_ReadsItAtThePathOfTheDocumentation()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"pos-1","Status":"Aprobada","Cuv":"cude"}""");

        // Act
        await nitro.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.PosTicket, "pos-1", null, "key"), CancellationToken.None);

        // Assert
        Assert.Equal("/v2/documents/equivalent-document/pos-1", nitro.Requests.Single().Path);
    }

    [Fact]
    public async Task GetFilesAsync_RelativeUrlsWithoutBase_LeavesTheFilesInTheProvider()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"doc-1","Prefix":"SETP","Consecutive":1,"UrlPdf":"1394/transactions/2026/2/invoices/fv.pdf"}""");

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "doc-1", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(((ProviderFile?)null, 1), (files.Pdf, nitro.Requests.Count));
    }

    [Fact]
    public async Task GetFilesAsync_RelativeUrlsWithBase_DownloadsThem()
    {
        // Arrange
        var options = FakeAliaddo.Options();

        options.FilesBaseUrl = new Uri("https://files.example.test/");
        nitro.Answer = request => request.RequestUri!.Host == "files.example.test"
            ? (HttpStatusCode.OK, "%PDF-1.4")
            : (HttpStatusCode.OK, """{"Id":"doc-1","Prefix":"SETP","Consecutive":1,"UrlPdf":"1394/fv.pdf"}""");

        // Act
        var files = await nitro.Provider(options).GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "doc-1", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(("SETP1.pdf", "%PDF-1.4"), (files.Pdf!.Name, Encoding.UTF8.GetString(files.Pdf.Content)));
    }

    [Fact]
    public async Task ProvisionAsync_Issuer_CreatesTheChildWithItsTaxIdSplit()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, """{"id":"f5cdc13a-55c6-4b1c-8a44-8ec7b68f38cd","contractSignatureStatus":"pending","enabled":false}""");

        // Act
        var provisioned = await nitro.Provider().ProvisionAsync(Issuer(), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        var company = nitro.LastBody.GetProperty("company");

        Assert.Equal(("/companies/isv", "900000002", "1", "31", "f5cdc13a-55c6-4b1c-8a44-8ec7b68f38cd", "3000000002"), (nitro.Requests.Single().Path, company.GetProperty("identificationNumber").GetString(), company.GetProperty("identificationCheck").GetString(), company.GetProperty("identificationTypeId").GetString(), provisioned.ProviderIssuerId, nitro.LastBody.GetProperty("user").GetProperty("phoneNumber").GetString()));
    }

    [Theory]
    [InlineData("""{"Key":"k","Prefix":"SETT","Number":1234567890,"RangeInitial":1,"RangeFinal":1000,"ValidFrom":"2026-05-05T00:00:00","ValidUntil":"2028-05-05T00:00:00"}""")]
    [InlineData("""[{"Key":"k","Prefix":"SETT","Number":1234567890,"RangeInitial":1,"RangeFinal":1000,"ValidFrom":"2026-05-05T00:00:00","ValidUntil":"2028-05-05T00:00:00"}]""")]
    public async Task GetNumberingRangesAsync_OneOrSeveral_ReadsTheResolution(string body)
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, body);

        // Act
        var range = (await nitro.Provider().GetNumberingRangesAsync(Issuer(), CancellationToken.None)).Single();

        // Assert
        Assert.Equal(new ProviderNumberingRange("k", "SETT", "1234567890", 1, 1000, new LocalDate(2026, 5, 5), new LocalDate(2028, 5, 5)), range);
    }

    [Fact]
    public void Capabilities_WebhooksOff_AsksForTheResults()
    {
        // Act & Assert
        Assert.Equal(StatusUpdatesMode.Poll, nitro.Provider().Capabilities.StatusUpdates);
    }

    [Theory]
    [InlineData("hex", null)]
    [InlineData("base64", null)]
    [InlineData("hex", "sha256=")]
    public void IsAuthentic_SignatureOfTheConfiguredSecret_IsTrusted(string encoding, string? prefix)
    {
        // Arrange
        var options = Webhooks(encoding, prefix);
        var body = """{"Id":"doc-1","Status":"Aprobada"}""";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes("invented-webhook-secret"), Encoding.UTF8.GetBytes(body));
        var signature = (prefix ?? "") + (encoding == "hex" ? Convert.ToHexStringLower(mac) : Convert.ToBase64String(mac));

        // Act
        var authentic = nitro.Provider(options).IsAuthentic(body, new Dictionary<string, string> { ["x-invented-signature"] = signature });

        // Assert
        Assert.True(authentic);
    }

    [Fact]
    public void IsAuthentic_BodyChanged_IsNotTrusted()
    {
        // Arrange
        var options = Webhooks("hex", null);
        var mac = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("invented-webhook-secret"), Encoding.UTF8.GetBytes("""{"Id":"doc-1"}""")));

        // Act
        var authentic = nitro.Provider(options).IsAuthentic("""{"Id":"doc-2"}""", new Dictionary<string, string> { ["x-invented-signature"] = mac });

        // Assert
        Assert.False(authentic);
    }

    [Fact]
    public void IsAuthentic_WebhooksOff_IsNeverTrusted()
    {
        // Act
        var authentic = nitro.Provider().IsAuthentic("{}", new Dictionary<string, string>());

        // Assert
        Assert.False(authentic);
    }

    [Fact]
    public async Task ReadAsync_ConfiguredProperty_NamesTheDocument()
    {
        // Act
        var read = await nitro.Provider(Webhooks("hex", null)).ReadAsync("""{"Id":"doc-1","Status":"Aprobada"}""", new Dictionary<string, string>(), CancellationToken.None);

        // Assert
        Assert.Equal(new ProviderDocumentEvent("doc-1", "Aprobada"), read);
    }

    private static AliaddoOptions Webhooks(string encoding, string? prefix)
    {
        var options = FakeAliaddo.Options();

        options.Webhooks = new AliaddoWebhooksOptions { Enabled = true, SignatureHeader = "X-Invented-Signature", Secret = "invented-webhook-secret", Encoding = encoding, SignaturePrefix = prefix, DocumentIdProperty = "Id", StatusProperty = "Status" };

        return options;
    }
}

public class AliaddoMapperTest
{
    [Theory]
    [InlineData(1_000_049, 2, 10000)]
    [InlineData(1_000_050, 2, 10001)]
    [InlineData(-1_000_050, 2, -10001)]
    [InlineData(1500, 0, 1500)]
    [InlineData(1_234_567, 3, 1235)]
    public void Whole_MinorUnits_RoundsHalfAwayFromZeroWithTheDecimalsOfTheCurrency(long minor, short digits, long expected)
    {
        // Act & Assert
        Assert.Equal(expected, AliaddoMapper.Whole(minor, digits));
    }

    [Fact]
    public void Percentage_BasisPoints_IsThePercentage()
    {
        // Act & Assert
        Assert.Equal(2.5m, AliaddoMapper.Percentage(250));
    }
}
