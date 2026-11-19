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

    private static readonly LocalDate IssueDate = new(2026, 11, 10);

    private static ProviderNumberingRange Range(string number = "18760000001") => new("technical-key-of-test", "SETP", number, 990000000, 995000000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    private static ProviderDocument Document(ElectronicDocumentType type = ElectronicDocumentType.Invoice, DocumentContent? content = null, ProviderNumberingRange? range = null) => new(Guid.NewGuid(), type, Number, range ?? Range(), content ?? TestData.ContentOf(type), "key", IssueDate);

    private async Task<ProviderSubmission> SubmitAsync(ProviderDocument? document = null, ProviderIssuer? issuer = null) => await nitro.Provider().SubmitAsync(issuer ?? Issuer(), document ?? Document(), CancellationToken.None);

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice, "/v2/documents/invoices")]
    [InlineData(ElectronicDocumentType.CreditNote, "/v2/documents/invoices/credit-notes")]
    [InlineData(ElectronicDocumentType.DebitNote, "/v2/documents/invoices/debit-notes")]
    [InlineData(ElectronicDocumentType.PosTicket, "/v2/documents/equivalent-documents")]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote, "/v2/documents/equivalent-documents/credit-notes")]
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

        Assert.Equal(("01", "10", "Test", "SETP", 990000000L, 18760000001L, "technical-key-of-test", "2026-11-10"), (body.GetProperty("code").GetString(), body.GetProperty("typeOperation").GetString(), body.GetProperty("mode").GetString(), body.GetProperty("prefix").GetString(), body.GetProperty("consecutive").GetInt64(), body.GetProperty("resolution").GetProperty("number").GetInt64(), body.GetProperty("resolution").GetProperty("key").GetString(), body.GetProperty("dueAt").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_PosAdjustmentNote_GoesAsTheCreditNoteOfThePosTicket()
    {
        // Arrange: Nitro corrects a ticket with its credit adjustment note (code 94) on a POS ticket (operation 20).
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosAdjustmentNote));

        // Assert
        var body = nitro.LastBody;

        Assert.Equal(("94", "20", "SETP", 990000001L), (body.GetProperty("code").GetString(), body.GetProperty("typeOperation").GetString(), body.GetProperty("invoicePrefix").GetString(), body.GetProperty("invoiceConsecutive").GetInt64()));
    }

    [Fact]
    public async Task SubmitAsync_PosTicket_SendsTheCashierAsTheSellerAndThePlaceOfTheRegisterAsTheBranch()
    {
        // Arrange: the name and the address are the ones of the register; the department and the municipality, the issuer's.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosTicket));

        // Assert
        var body = nitro.LastBody;
        var branch = body.GetProperty("branch");

        Assert.Equal(("Cajera de Prueba", "Punto de Prueba", "Calle 9 # 8-7", "11", "Bogota", "11001", "Bogota"), (body.GetProperty("sellerName").GetString(), branch.GetProperty("name").GetString(), branch.GetProperty("address").GetString(), branch.GetProperty("regionCode").GetString(), branch.GetProperty("regionName").GetString(), branch.GetProperty("cityCode").GetString(), branch.GetProperty("cityName").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_PosTicket_SendsNoFieldTheSchemaOfNitroDoesNotHave()
    {
        // Arrange: the plate, the type of register and the code of the sale have no documented field; they are not invented.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosTicket));

        // Assert
        var sent = nitro.LastBody.GetRawText();

        Assert.Equal((false, false, false, 6), (sent.Contains("REG-001"), sent.Contains("Standard"), sent.Contains("SALE-0001"), nitro.LastBody.GetProperty("branch").EnumerateObject().Count()));
    }

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice)]
    [InlineData(ElectronicDocumentType.CreditNote)]
    [InlineData(ElectronicDocumentType.DebitNote)]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote)]
    public async Task SubmitAsync_DocumentWithoutARegister_SendsNeitherSellerNorBranch(ElectronicDocumentType type)
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(type));

        // Assert
        Assert.Equal((false, false), (nitro.LastBody.TryGetProperty("sellerName", out _), nitro.LastBody.TryGetProperty("branch", out _)));
    }

    [Fact]
    public async Task SubmitAsync_PosAdjustmentNoteWithItsRegister_SendsTheSellerAndTheBranch()
    {
        // Arrange: the credit note of the ticket documents the same two fields.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosAdjustmentNote, TestData.NoteContent() with { PointOfSale = TestData.PointOfSale() }));

        // Assert
        Assert.Equal(("Cajera de Prueba", "Punto de Prueba"), (nitro.LastBody.GetProperty("sellerName").GetString(), nitro.LastBody.GetProperty("branch").GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote, "/v2/documents/equivalent-documents/credit-note/find-by-prefix-and-consecutive")]
    [InlineData(ElectronicDocumentType.PosTicket, "/v2/documents/equivalent-documents/find-by-prefix-and-consecutive")]
    public void ByNumberOf_EquivalentDocuments_GoesToTheirOwnPath(ElectronicDocumentType type, string path)
    {
        // Act
        var found = AliaddoPaths.ByNumberOf(type, Number);

        // Assert
        Assert.Equal(path, found.Split('?')[0]);
    }

    [Fact]
    public async Task SubmitAsync_BuyerWithoutNames_SendsItsWholeNameAsTheFirstNameAndNoLastName()
    {
        // Arrange: Nitro shows the buyer as the first and last names joined; a company has neither.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync();

        // Assert
        var customer = nitro.LastBody.GetProperty("customer");

        Assert.Equal(("Comprador de Prueba SAS", "Comprador de Prueba SAS", string.Empty), (customer.GetProperty("companyName").GetString(), customer.GetProperty("firstName").GetString(), customer.GetProperty("lastName").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_PersonWithNames_SendsThemApart()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);
        var person = TestData.Buyer() with { PersonTypeCode = "2", IdentificationTypeCode = "13", CheckDigit = null, Name = "Ana María Pérez Gómez", FirstName = "Ana María", LastName = "Pérez Gómez" };

        // Act
        await SubmitAsync(Document(content: TestData.InvoiceContent() with { Buyer = person }));

        // Assert
        var customer = nitro.LastBody.GetProperty("customer");

        Assert.Equal(("Ana María", "Pérez Gómez"), (customer.GetProperty("firstName").GetString(), customer.GetProperty("lastName").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_InCash_SendsTheDayItIsSentAsTheDueDate()
    {
        // Arrange: a cash sale keeps no due date; Nitro issues it on the day it receives it.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(content: TestData.InvoiceContent() with { DueDate = null }));

        // Assert
        Assert.Equal("2026-11-10", nitro.LastBody.GetProperty("dueAt").GetString());
    }

    [Fact]
    public async Task SubmitAsync_OnCredit_SendsItsDueDate()
    {
        // Arrange
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(content: TestData.InvoiceContent() with { PaymentMeans = new PaymentMeans("2", "10"), DueDate = new LocalDate(2026, 12, 10) }));

        // Assert
        Assert.Equal("2026-12-10", nitro.LastBody.GetProperty("dueAt").GetString());
    }

    [Fact]
    public async Task SubmitAsync_DueBeforeTheDayItIsSent_SendsTheDayOfIssue()
    {
        // Arrange: the case of SETP991148967, issued on the 10th and due on the 9th because the caller dated it on the payment.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync(Document(content: TestData.InvoiceContent() with { DueDate = new LocalDate(2026, 11, 9) }));

        // Assert
        Assert.Equal("2026-11-10", nitro.LastBody.GetProperty("dueAt").GetString());
    }

    [Fact]
    public async Task SubmitAsync_Invoice_SendsTheIntegerAmountsInWholeUnitsAndTheRatesAsPercentages()
    {
        // Arrange: 2 units of 10.000,00 with 19 % VAT, in minor units of a currency with two decimals. The tax of the
        // line is 3.800,00 over 20.000,00; Nitro takes it per unit and multiplies it by the quantity.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        // Act
        await SubmitAsync();

        // Assert
        var line = nitro.LastBody.GetProperty("lines")[0];
        var tax = line.GetProperty("taxes")[0];
        var totals = nitro.LastBody.GetProperty("totals");

        Assert.Equal((10000L, 2L, 19m, 1900m, 10000m, 20000L, 3800L), (line.GetProperty("price").GetInt64(), line.GetProperty("quantity").GetInt64(), tax.GetProperty("rate").GetDecimal(), tax.GetProperty("amount").GetDecimal(), tax.GetProperty("base").GetDecimal(), totals.GetProperty("amount").GetInt64(), totals.GetProperty("taxesAmount").GetInt64()));
    }

    [Fact]
    public async Task SubmitAsync_TaxThatDoesNotDivideByTheQuantity_GivesTheTaxOfTheLineAgainTimesTheQuantity()
    {
        // Arrange: 3 units of 333,00 with 19 % VAT; the tax of the line is 189,81 over 999,00.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        var line = TestData.Line() with { Quantity = 3, UnitPrice = 33_300, Amount = 99_900, Taxes = [new LineTax("01", "IVA", 1900, 99_900, 18_981)], Withholdings = [new LineTax("06", "ReteFuente", 250, 99_900, 2_498)] };
        var content = TestData.InvoiceContent() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 99_900, 0, 18_981, 2_498, 0, 116_383) };

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        var sent = nitro.LastBody.GetProperty("lines")[0];
        var tax = sent.GetProperty("taxes")[0];
        var withholding = sent.GetProperty("withholdings")[0];

        Assert.Equal((333m, 63.27m, 189.81m, 8.326667m, 24.98m), (tax.GetProperty("base").GetDecimal(), tax.GetProperty("amount").GetDecimal(), Math.Round(tax.GetProperty("amount").GetDecimal() * 3, 2), withholding.GetProperty("amount").GetDecimal(), Math.Round(withholding.GetProperty("amount").GetDecimal() * 3, 2)));
    }

    [Fact]
    public async Task SubmitAsync_LineWithDiscount_SendsTheDiscountPerUnitWithoutLosingCents()
    {
        // Arrange: 3 units of 10.000,00 with a discount of 1.000,00 on the line.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        var line = TestData.Line() with { Quantity = 3, DiscountAmount = 100_000, Amount = 2_900_000, Taxes = [new LineTax("01", "IVA", 1900, 2_900_000, 551_000)] };
        var content = TestData.InvoiceContent() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 3_000_000, 100_000, 551_000, 0, 0, 3_451_000) };

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        var sent = nitro.LastBody.GetProperty("lines")[0];
        var discount = sent.GetProperty("discounts")[0].GetProperty("amount").GetDecimal();

        Assert.Equal((1000L, 333.333333m, 1000m), (sent.GetProperty("discountAmount").GetInt64(), discount, Math.Round(discount * 3, 2)));
    }

    [Fact]
    public async Task SubmitAsync_NoteWithItsOwnPrefix_SendsTheResolutionOfItsInvoiceWithThePrefixOfTheNote()
    {
        // Arrange: the note NC1 corrects an invoice of the resolution whose prefix is SETP.
        nitro.Answer = _ => (HttpStatusCode.OK, Approved);

        var note = new ProviderDocument(Guid.NewGuid(), ElectronicDocumentType.CreditNote, new DocumentNumber("NC", 1), Range(), TestData.NoteContent(), "key", IssueDate);

        // Act
        await SubmitAsync(note);

        // Assert
        var body = nitro.LastBody;

        Assert.Equal(("NC", 1L, "NC", 18760000001L, "SETP"), (body.GetProperty("prefix").GetString(), body.GetProperty("consecutive").GetInt64(), body.GetProperty("resolution").GetProperty("prefix").GetString(), body.GetProperty("resolution").GetProperty("number").GetInt64(), body.GetProperty("invoicePrefix").GetString()));
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

    private const string Found = """{"Id":"doc-1","Prefix":"SETT","Consecutive":2416024,"Sequence":"SETT2416024","UrlPdf":"42707/transactions/2026/11/invoices/fv.pdf","UrlXmlDocument":"/tmp/App_File/Temp/2026/42707/transactions/11/invoices/fv.xml","UrlXmlAttachedDocument":"/tmp/App_File/Temp/2026/42707/transactions/11/invoices/ad.xml"}""";

    private static readonly ProviderDocumentLookup FoundLookup = new(ElectronicDocumentType.Invoice, "doc-1", Number, "key");

    [Fact]
    public async Task GetFilesAsync_Pdf_IsDownloadedFromTheApiWithItsKeyItsSequenceAndTheToken()
    {
        // Arrange
        nitro.Answer = request => request.RequestUri!.AbsolutePath == "/panel/download"
            ? request.RequestUri.Query.Contains("fv.pdf") ? (HttpStatusCode.OK, "%PDF-1.4") : (HttpStatusCode.NotFound, "")
            : (HttpStatusCode.OK, Found);

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), FoundLookup, CancellationToken.None);

        // Assert
        var download = nitro.Requests.Single(x => x.Path.Contains("fv.pdf"));

        Assert.Equal(("SETT2416024.pdf", "%PDF-1.4", "/panel/download?urlKey=42707%2Ftransactions%2F2026%2F11%2Finvoices%2Ffv.pdf&sequence=SETT2416024", "Bearer invented-master-token"), (files.Pdf!.Name, Encoding.UTF8.GetString(files.Pdf.Content), download.Path, download.Authorization));
    }

    [Fact]
    public async Task GetFilesAsync_XmlOfAnInvoice_IsDownloadedFromNextToThePdfNotFromItsTemporaryPath()
    {
        // Arrange: an invoice names its XML with the temporary path where Nitro built them; the files are next to the PDF.
        nitro.Answer = request => request.RequestUri!.AbsolutePath != "/panel/download"
            ? (HttpStatusCode.OK, Found)
            : request.RequestUri.Query.Contains("urlKey=42707%2Ftransactions%2F2026%2F11%2Finvoices%2Fad.xml") ? (HttpStatusCode.OK, "<AttachedDocument/>")
            : request.RequestUri.Query.Contains("urlKey=42707%2Ftransactions%2F2026%2F11%2Finvoices%2Ffv.xml") ? (HttpStatusCode.OK, "<Invoice/>")
            : (HttpStatusCode.NotFound, "");

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), FoundLookup, CancellationToken.None);

        // Assert
        Assert.Equal(("SETT2416024.xml", "<Invoice/>", "SETT2416024-attached.xml", "<AttachedDocument/>", false), (files.Xml!.Name, Encoding.UTF8.GetString(files.Xml.Content), files.AttachedDocument!.Name, Encoding.UTF8.GetString(files.AttachedDocument.Content), nitro.Requests.Any(x => x.Path.Contains("tmp"))));
    }

    [Theory]
    [InlineData("42707/transactions/2026/11/invoice-credit-notes/nc.xml", "42707/transactions/2026/11/invoice-credit-notes/nc.pdf", "42707/transactions/2026/11/invoice-credit-notes/nc.xml")]
    [InlineData("/tmp/App_File/Temp/2026/42707/transactions/11/invoices/fv.xml", "42707/transactions/2026/11/invoices/fv.pdf", "42707/transactions/2026/11/invoices/fv.xml")]
    [InlineData("/tmp/App_File/fv.xml", null, "/tmp/App_File/fv.xml")]
    [InlineData("/tmp/App_File/fv.xml", "fv.pdf", "/tmp/App_File/fv.xml")]
    [InlineData(null, "42707/fv.pdf", null)]
    public void KeyOf_File_IsItsRealKeyOrTheNameNextToThePdfWhenItIsATemporaryPath(string? key, string? pdfKey, string? expected)
    {
        // Act & Assert
        Assert.Equal(expected, AliaddoProvider.KeyOf(key, pdfKey));
    }

    [Fact]
    public async Task GetFilesAsync_KeyTheApiDoesNotHave_FollowsTheSignedLinkOfTheStorageWithoutTheToken()
    {
        // Arrange: for a key it does not have, the API answers a JSON text with a signed temporary link to another host.
        nitro.Answer = request => request.RequestUri!.Host == "storage.example.test"
            ? (HttpStatusCode.OK, "<AttachedDocument/>")
            : request.RequestUri.AbsolutePath == "/panel/download"
                ? request.RequestUri.Query.Contains("ad.xml") ? (HttpStatusCode.OK, "\"https://storage.example.test/tmp/ad.xml?X-Amz-Signature=invented\"") : (HttpStatusCode.NotFound, "")
                : (HttpStatusCode.OK, Found);

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), FoundLookup, CancellationToken.None);

        // Assert
        var storage = nitro.Requests.Single(x => x.Path.StartsWith("/tmp/ad.xml"));

        Assert.Equal(("SETT2416024-attached.xml", "<AttachedDocument/>", (string?)null, (ProviderFile?)null), (files.AttachedDocument!.Name, Encoding.UTF8.GetString(files.AttachedDocument.Content), storage.Authorization, files.Pdf));
    }

    [Theory]
    [InlineData("\"http://storage.example.test/ad.xml\"")]
    [InlineData("\"not a link\"")]
    [InlineData("""{"message":"El documento no tiene archivos."}""")]
    public async Task GetFilesAsync_AnswerThatIsNotASecureLink_LeavesTheFileInTheProvider(string answer)
    {
        // Arrange
        nitro.Answer = request => request.RequestUri!.AbsolutePath == "/panel/download" ? (HttpStatusCode.OK, answer) : (HttpStatusCode.OK, Found);

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), FoundLookup, CancellationToken.None);

        // Assert: the document and one download for each of its three files, and nothing followed.
        Assert.Equal(((ProviderFile?)null, (ProviderFile?)null, (ProviderFile?)null, 4), (files.Xml, files.AttachedDocument, files.Pdf, nitro.Requests.Count));
    }

    [Fact]
    public async Task GetFilesAsync_DocumentWithoutFiles_AsksForNone()
    {
        // Arrange: a rejected document names no PDF.
        nitro.Answer = _ => (HttpStatusCode.OK, """{"Id":"doc-1","Prefix":"SETT","Consecutive":1,"UrlPdf":null}""");

        // Act
        var files = await nitro.Provider().GetFilesAsync(Issuer(), FoundLookup, CancellationToken.None);

        // Assert
        Assert.Equal(((ProviderFile?)null, 1), (files.Pdf, nitro.Requests.Count));
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

    private const string WebhookSecret = "invented-webhook-secret";

    private static readonly Dictionary<string, string> NoHeaders = [];

    /// <summary>
    /// A call as Aliaddo sends it: the signature in the body, over the timestamp followed by the token.
    /// </summary>
    private string Call(string secret = WebhookSecret, long? timestamp = null, string token = "8c1f0f0e-6c0b-4f3e-9a51-4b9f8f2d7a10", string status = "Aprobada", bool timestampAsText = false)
    {
        var seconds = (timestamp ?? nitro.Now.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var mac = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(seconds + token)));

        object stamp = timestampAsText ? seconds : long.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(new
        {
            signature = new { timestamp = stamp, token, signature = mac },
            eventData = new { id = "doc-1", cude = "cude", sequence = "SETT1", qr = "qr", status, statusReasons = Array.Empty<string>() }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsAuthentic_CallSignedWithTheKeyOfThePortal_IsTrusted(bool timestampAsText)
    {
        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(Call(timestampAsText: timestampAsText), NoHeaders);

        // Assert
        Assert.True(authentic);
    }

    [Fact]
    public void IsAuthentic_SignatureInUpperCase_IsTrusted()
    {
        // Arrange
        var call = Call();
        var signature = JsonDocument.Parse(call).RootElement.GetProperty("signature").GetProperty("signature").GetString()!;

        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(call.Replace(signature, signature.ToUpperInvariant()), NoHeaders);

        // Assert
        Assert.True(authentic);
    }

    [Fact]
    public void IsAuthentic_SignedWithAnotherKey_IsNotTrusted()
    {
        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(Call(secret: "another-secret"), NoHeaders);

        // Assert
        Assert.False(authentic);
    }

    [Fact]
    public void IsAuthentic_TokenChanged_IsNotTrusted()
    {
        // Arrange
        var call = Call(token: "token-a").Replace("token-a", "token-b");

        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(call, NoHeaders);

        // Assert
        Assert.False(authentic);
    }

    [Theory]
    [InlineData(-299, true)]
    [InlineData(-301, false)]
    [InlineData(299, true)]
    [InlineData(301, false)]
    public void IsAuthentic_Timestamp_IsTrustedOnlyWithinTheTolerance(int secondsFromNow, bool expected)
    {
        // Arrange: the signature is right; only its age changes. The tolerance is five minutes.
        var call = Call(timestamp: nitro.Now.ToUnixTimeSeconds() + secondsFromNow);

        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(call, NoHeaders);

        // Assert
        Assert.Equal(expected, authentic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"eventData":{"id":"doc-1"}}""")]
    [InlineData("""{"signature":"text","eventData":{"id":"doc-1"}}""")]
    [InlineData("""{"signature":{"timestamp":"soon","token":"t","signature":"s"}}""")]
    [InlineData("""{"signature":{"timestamp":99999999999999999,"token":"t","signature":"s"}}""")]
    [InlineData("""{"signature":{"timestamp":1794405569,"signature":"s"}}""")]
    public void IsAuthentic_CallWithoutAReadableSignature_IsNotTrusted(string body)
    {
        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(body, NoHeaders);

        // Assert
        Assert.False(authentic);
    }

    [Fact]
    public void IsAuthentic_CallOfTheTesterOfThePortal_MatchesTheAlgorithm()
    {
        // Arrange: the shape and the arithmetic of a real call (11-nov-2026), with an invented key and token.
        const string Body = """{"signature":{"timestamp":1794405569,"token":"00000000-0000-4000-8000-000000000000","signature":"a2df3de0aa8b4c0ecd2c3ef4d29e6a56b27d6b27b5a9bd3a8b2f6d2dc8a0f0f0"},"eventData":{"id":"doc-1","status":"Aprobada"}}""";

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes("179440556900000000-0000-4000-8000-000000000000")));

        nitro.Now = Instant.FromUnixTimeSeconds(1794405570);

        // Act
        var authentic = nitro.Provider(Webhooks()).IsAuthentic(Body.Replace("a2df3de0aa8b4c0ecd2c3ef4d29e6a56b27d6b27b5a9bd3a8b2f6d2dc8a0f0f0", expected), NoHeaders);

        // Assert
        Assert.True(authentic);
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
    public async Task ReadAsync_Call_NamesTheDocumentOfItsEventData()
    {
        // Act
        var read = await nitro.Provider(Webhooks()).ReadAsync(Call(status: "Rechazada"), NoHeaders, CancellationToken.None);

        // Assert
        Assert.Equal(new ProviderDocumentEvent("doc-1", "Rechazada"), read);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"ping":true}""")]
    [InlineData("""{"eventData":{"status":"Aprobada"}}""")]
    [InlineData("""{"Id":"doc-1","Status":"Aprobada"}""")]
    public async Task ReadAsync_CallThatNamesNoDocument_IsNothing(string body)
    {
        // Act
        var read = await nitro.Provider(Webhooks()).ReadAsync(body, NoHeaders, CancellationToken.None);

        // Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData(true, null, 5, 1)]
    [InlineData(true, WebhookSecret, 0, 1)]
    [InlineData(true, WebhookSecret, 5, 0)]
    [InlineData(false, null, 5, 0)]
    public void Validate_Webhooks_NeedTheirKeyWhenTheyAreOnAndATolerance(bool enabled, string? secret, int toleranceMinutes, int expectedErrors)
    {
        // Arrange
        var options = FakeAliaddo.Options();

        options.Webhooks = new AliaddoWebhooksOptions { Enabled = enabled, Secret = secret, Tolerance = TimeSpan.FromMinutes(toleranceMinutes) };

        // Act
        var errors = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        // Assert
        Assert.Equal(expectedErrors, errors.Count);
    }

    private static AliaddoOptions Webhooks()
    {
        var options = FakeAliaddo.Options();

        options.Webhooks = new AliaddoWebhooksOptions { Enabled = true, Secret = WebhookSecret };

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
