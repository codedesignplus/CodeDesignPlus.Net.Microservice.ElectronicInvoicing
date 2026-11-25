using System.Text;
using System.Text.RegularExpressions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;
using InfraErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Alanube;

public class AlanubeProviderTest
{
    private static readonly DocumentNumber Number = new("SETP", 990641077);

    private readonly FakeAlanube alanube = new();

    private static ProviderIssuer Issuer(string? companyId = FakeAlanube.CompanyId) => ProviderIssuer.From(TestData.EnabledIssuer("Alanube")) with { ProviderIssuerId = companyId };

    private static readonly LocalDate IssueDate = new(2026, 11, 10);

    private static ProviderNumberingRange Range(string? technicalKey = "technical-key-of-test") => new(technicalKey, "SETP", "18760000001", 990000000, 995000000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    private static ProviderDocument Document(ElectronicDocumentType type = ElectronicDocumentType.Invoice, DocumentContent? content = null, ProviderNumberingRange? range = null, Guid? id = null, int revision = 0) => new(id ?? Guid.NewGuid(), type, Number, range ?? Range(), content ?? TestData.ContentOf(type), "key", IssueDate) { Revision = revision };

    private async Task<ProviderSubmission> SubmitAsync(ProviderDocument? document = null, ProviderIssuer? issuer = null, AlanubeOptions? options = null) => await alanube.Provider(options).SubmitAsync(issuer ?? Issuer(), document ?? Document(), CancellationToken.None);

    private static string? Text(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            element = element.GetProperty(name);
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
    }

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice, "/e-provider/col/v1/invoices")]
    [InlineData(ElectronicDocumentType.CreditNote, "/e-provider/col/v1/credit-notes")]
    [InlineData(ElectronicDocumentType.DebitNote, "/e-provider/col/v1/debit-notes")]
    [InlineData(ElectronicDocumentType.PosTicket, "/e-provider/col/v1/equivalent-documents/pos")]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote, "/e-provider/col/v1/adjustment-note-equivalent-documents")]
    public async Task SubmitAsync_EachType_PostsToItsPathWithTheMainToken(ElectronicDocumentType type, string path)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(type));

        // Assert
        var call = alanube.Calls.Single();

        Assert.Equal((HttpMethod.Post, path, "Bearer invented-main-token"), (call.Method, call.PathAndQuery, call.Authorization));
    }

    [Fact]
    public async Task SubmitAsync_Invoice_SendsItsTypeNumberAndResolution()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync();

        // Assert
        var body = alanube.LastBody;

        Assert.Equal(("01", "990641077", "18760000001", "SETP", "990000000", "995000000", "2026-01-01", "2027-12-31", "technical-key-of-test"), (Text(body, "documentType"), Text(body, "number"), Text(body, "resolution", "resolutionNumber"), Text(body, "resolution", "prefix"), Text(body, "resolution", "minNumber"), Text(body, "resolution", "maxNumber"), Text(body, "resolution", "startDate"), Text(body, "resolution", "endDate"), Text(body, "resolution", "technicalKey")));
    }

    [Fact]
    public async Task SubmitAsync_AnyDocument_SendsTheWholeDataOfTheIssuer()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync();

        // Assert
        var company = alanube.LastBody.GetProperty("company");

        Assert.Equal((FakeAlanube.CompanyId, "1", "31", "900000002", "1", "Emisor de Prueba SAS", "O-13", "01", "emisor@example.com", "11001", "11", "CO", "110111"), (Text(company, "id"), Text(company, "organizationType"), Text(company, "identificationType"), Text(company, "identificationNumber"), Text(company, "dv"), Text(company, "name"), Text(company, "regimeCode"), Text(company, "taxCode", "id"), Text(company, "email"), Text(company, "address", "city"), Text(company, "address", "department"), Text(company, "address", "country"), Text(company, "address", "postalCode")));
    }

    [Fact]
    public async Task SubmitAsync_Invoice_SendsTheAmountsInUnitsOfTheCurrencyAndTheRatesAsText()
    {
        // Arrange: 2 units of 10.000,00 with 19 % VAT, in minor units of a currency with two decimals.
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync();

        // Assert
        var item = alanube.LastBody.GetProperty("items")[0];
        var tax = item.GetProperty("taxes")[0];

        Assert.Equal((10000m, 2m, 20000m, 3800m, 23800m, "01", "19.00", 3800m, 20000m), (item.GetProperty("price").GetDecimal(), item.GetProperty("quantity").GetDecimal(), item.GetProperty("subtotal").GetDecimal(), item.GetProperty("taxAmount").GetDecimal(), item.GetProperty("total").GetDecimal(), tax.GetProperty("taxCode").GetString(), tax.GetProperty("taxPercentage").GetString(), tax.GetProperty("taxAmount").GetDecimal(), tax.GetProperty("taxableAmount").GetDecimal()));
    }

    [Fact]
    public async Task SubmitAsync_InvoiceWithAPrepayment_PayableKeepsTheAdvanceApart()
    {
        // Arrange: the tax authority rejects a payable total with the advance subtracted (rule FAU14, seen in the sandbox).
        var content = TestData.InvoiceContent() with { Prepayments = [new Prepayment("ANT-1", 500_000, new LocalDate(2026, 11, 1))], Totals = TestData.Totals() with { PrepaymentAmount = 500_000, PayableAmount = 1_880_000 } };

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        var totals = alanube.LastBody.GetProperty("totalAmounts");
        var advance = alanube.LastBody.GetProperty("advancePayments")[0];

        Assert.Equal((20000m, 20000m, 3800m, 5000m, 23800m, "ANT-1", 5000m, "2026-11-01"), (totals.GetProperty("grossTotal").GetDecimal(), totals.GetProperty("taxableTotal").GetDecimal(), totals.GetProperty("taxTotal").GetDecimal(), totals.GetProperty("advanceTotal").GetDecimal(), totals.GetProperty("payableTotal").GetDecimal(), advance.GetProperty("advanceId").GetString(), advance.GetProperty("advanceAmount").GetDecimal(), advance.GetProperty("advanceReceivedDate").GetString()));
    }

    [Fact]
    public async Task SubmitAsync_LineWithADiscount_LowersItsSubtotalAndNotTheDocument()
    {
        // Arrange: 2 units of 10.000,00 with 5.000,00 off, and the VAT on the 15.000,00 left.
        var line = new DocumentLine("SVC-1", "Servicio de prueba", 2, "94", 1_000_000, 500_000, 1_500_000, [new LineTax("01", "IVA", 1900, 1_500_000, 285_000)], []);
        var content = TestData.InvoiceContent() with { Lines = [line], Totals = new DocumentTotals("COP", 2, 2_000_000, 500_000, 285_000, 0, 0, 1_785_000) };

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        var item = alanube.LastBody.GetProperty("items")[0];
        var totals = alanube.LastBody.GetProperty("totalAmounts");

        Assert.Equal((25m, 5000m, 15000m, 15000m, 0m, 17850m), (item.GetProperty("discount").GetDecimal(), item.GetProperty("discountAmount").GetDecimal(), item.GetProperty("subtotal").GetDecimal(), totals.GetProperty("grossTotal").GetDecimal(), totals.GetProperty("discountTotal").GetDecimal(), totals.GetProperty("payableTotal").GetDecimal()));
    }

    [Fact]
    public async Task SubmitAsync_OnCredit_SendsTheDueDate()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { PaymentMeans = new PaymentMeans("2", "10"), DueDate = new LocalDate(2026, 11, 30) };

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        Assert.Equal("2026-11-30", Text(alanube.LastBody.GetProperty("payments")[0], "paymentDueDate"));
    }

    [Fact]
    public async Task SubmitAsync_OnCreditDueBeforeTheDayItIsSent_SendsTheDayOfIssue()
    {
        // Arrange: received due on the 9th, it waited for the provider and is sent on the 10th.
        var content = TestData.InvoiceContent() with { PaymentMeans = new PaymentMeans("2", "10"), DueDate = new LocalDate(2026, 11, 9) };

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(content: content));

        // Assert
        Assert.Equal("2026-11-10", Text(alanube.LastBody.GetProperty("payments")[0], "paymentDueDate"));
    }

    [Fact]
    public async Task SubmitAsync_InCash_SendsNoDueDate()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync();

        // Assert
        Assert.False(alanube.LastBody.GetProperty("payments")[0].TryGetProperty("paymentDueDate", out _));
    }

    [Fact]
    public async Task SubmitAsync_CreditNote_RefersToTheInvoiceItCorrects()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.CreditNote));

        // Assert
        var body = alanube.LastBody;
        var reference = body.GetProperty("associatedDocuments")[0];

        Assert.Equal(("91", "SETP", "2", "2026-11-01", "01", "990000001", "SETP", "cufe-of-the-corrected-invoice", false), (Text(body, "documentType"), Text(body, "prefix"), Text(body, "conceptCode"), Text(reference, "date"), Text(reference, "documentType"), Text(reference, "number"), Text(reference, "prefix"), Text(reference, "uuid"), body.TryGetProperty("resolution", out _)));
    }

    [Fact]
    public async Task SubmitAsync_PosTicket_SendsItsNumberAsTextAndTheItemOfTheSeller()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.PosTicket);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosTicket));

        // Assert
        var body = alanube.LastBody;
        var item = body.GetProperty("items")[0];

        Assert.Equal((JsonValueKind.String, "SVC-1", false, false, false), (body.GetProperty("number").ValueKind, Text(item, "sellersItemIdentification", "id"), item.TryGetProperty("code", out _), body.GetProperty("resolution").TryGetProperty("technicalKey", out _), body.GetProperty("totalAmounts").TryGetProperty("advanceTotal", out _)));
    }

    [Fact]
    public async Task SubmitAsync_PosTicket_SendsItsRegisterWithTheSubtotalOfTheTotals()
    {
        // Arrange: 20.000,00 gross with a discount of 1.500,00; the subtotal is 18.500 before the taxes.
        var line = TestData.Line() with { DiscountAmount = 150_000, Amount = 1_850_000, Taxes = [new LineTax("01", "IVA", 1900, 1_850_000, 351_500)] };
        var content = TestData.PosTicketContent() with { Lines = [line], Totals = TestData.Totals() with { DiscountAmount = 150_000, TaxAmount = 351_500, PayableAmount = 2_201_500 } };

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.PosTicket);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosTicket, content));

        // Assert
        var register = alanube.LastBody.GetProperty("cashRegister");

        Assert.Equal(("REG-001", "Punto de Prueba", "Cajera de Prueba", "Standard", "SALE-0001", "18500", 6), (Text(register, "plate"), Text(register, "location"), Text(register, "cashier"), Text(register, "type"), Text(register, "saleCode"), Text(register, "subtotal"), register.EnumerateObject().Count()));
    }

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice)]
    [InlineData(ElectronicDocumentType.CreditNote)]
    public async Task SubmitAsync_DocumentWithoutARegister_SendsNoCashRegister(ElectronicDocumentType type)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(type));

        // Assert
        Assert.False(alanube.LastBody.TryGetProperty("cashRegister", out _));
    }

    [Fact]
    public async Task SubmitAsync_PosAdjustmentNoteWithItsRegister_SendsNoCashRegister()
    {
        // Arrange: the adjustment note of Alanube has no place for the register.
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.PosTicket);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosAdjustmentNote, TestData.NoteContent() with { PointOfSale = TestData.PointOfSale() }));

        // Assert
        Assert.False(alanube.LastBody.TryGetProperty("cashRegister", out _));
    }

    [Fact]
    public async Task SubmitAsync_PosAdjustmentNote_SendsTheDiscrepancyAndTheTicketItCorrects()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.PosTicket);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.PosAdjustmentNote));

        // Assert
        var body = alanube.LastBody;

        Assert.Equal(("2", "SETP990000001", "cufe-of-the-corrected-invoice", "2026-11-01", "SETP", JsonValueKind.String), (Text(body, "discrepancy", "responseCode"), Text(body, "documentReference", "fullNumber"), Text(body, "documentReference", "cude"), Text(body, "documentReference", "issueDate"), Text(body, "prefix"), body.GetProperty("number").ValueKind));
    }

    [Fact]
    public async Task SubmitAsync_Accepted_IsAcceptedWithItsCufeAndNumber()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal(("01M717Z8XMRHR2S9Z5Y55NFG8X", ProviderDocumentStatus.Accepted, "31b4253abce02ce00d2474c073bdbbccbe6d93a7ba862bb26f6af5eca2189b0a3961309ca98392dbd6b7bf907dbd1267", Number, new LocalDate(2026, 11, 9)), (submission.ProviderDocumentId, submission.State.Status, submission.State.Cufe, submission.State.Number, submission.State.IssueDate));
    }

    [Fact]
    public async Task SubmitAsync_AcceptedWithObservations_IsAcceptedAndKeepsTheObservations()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Invoice());

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, AlanubeAnswers.Observation, "SENT/ACCEPTED_WITH_OBSERVATIONS"), (submission.State.Status, submission.State.Reasons.Single(), submission.State.Raw));
    }

    [Fact]
    public async Task SubmitAsync_RejectedByTheTaxAuthority_IsRejectedWithItsRulesAndWithoutCufe()
    {
        // Arrange: the answer carries a CUFE also when it is rejected.
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.RejectedByRule90);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Dian, "Regla: 90, Rechazo: Documento procesado anteriormente.", (string?)null), (submission.State.Status, submission.State.RejectionOrigin!.Value, submission.State.Reasons.Single(), submission.State.Cufe));
    }

    [Fact]
    public async Task SubmitAsync_WithoutLegalStatus_IsStillProcessing()
    {
        // Arrange: the tax authority did not answer in time; Alanube answers 2xx without a legal status.
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Waiting);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Processing, "01M7190000000000000000WAIT"), (submission.State.Status, submission.ProviderDocumentId));
    }

    [Fact]
    public async Task SubmitAsync_Failed_IsARejectionOfTheProviderWithItsErrors()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Failed);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Provider, "EPR500: Communication Error"), (submission.State.Status, submission.State.RejectionOrigin!.Value, submission.State.Reasons.Single()));
    }

    [Theory]
    [InlineData("AEP6008", "The company is not authorized by government entity for electronic invoice")]
    [InlineData("AEP15008", "The company is not authorized by government entity for electronic equivalent document pos")]
    [InlineData("AEP6012", "The 'organizationType' attribute is required.")]
    [InlineData("AEP1002", "Company not found")]
    public async Task SubmitAsync_IssuerNotReady_ThrowsProviderIssuerNotReady(string code, string message)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.BadRequest, AlanubeAnswers.Errors(code, message));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderIssuerNotReady.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData("AEP6009", "The invoice with this number is already registered and accepted")]
    [InlineData("AEP6010", "The invoice with this number has already been sent and is in process")]
    public async Task SubmitAsync_NumberAlreadyReceived_AdoptsWhatAlanubeHasWithTheNumber(string code, string message)
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Post ? (HttpStatusCode.BadRequest, AlanubeAnswers.Errors(code, message)) : (HttpStatusCode.OK, AlanubeAnswers.List(AlanubeAnswers.RejectedByRule90, AlanubeAnswers.Accepted));

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal(("01M717Z8XMRHR2S9Z5Y55NFG8X", ProviderDocumentStatus.Accepted, $"/e-provider/col/v1/invoices?idCompany={FakeAlanube.CompanyId}&prefix=SETP&number=990641077"), (submission.ProviderDocumentId, submission.State.Status, alanube.Calls.Last().PathAndQuery));
    }

    [Fact]
    public async Task SubmitAsync_NumberAlreadyReceivedButNotListedYet_ThrowsProviderUnavailable()
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Post ? (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP6010", "in process")) : (HttpStatusCode.OK, """{ "metadata": {}, "invoices": [] }""");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_FirstSendStillRunning_AdoptsWhatAlanubeHasWithTheNumber()
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Post ? (HttpStatusCode.Conflict, AlanubeAnswers.Errors("IDEMPOTENCY_REQUEST_IN_PROGRESS", "in progress")) : (HttpStatusCode.OK, AlanubeAnswers.List(AlanubeAnswers.Accepted));

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, submission.State.Status);
    }

    [Fact]
    public async Task SubmitAsync_FirstSendStillRunningAndNotListed_ThrowsProviderUnavailable()
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Post ? (HttpStatusCode.Conflict, AlanubeAnswers.Errors("IDEMPOTENCY_OUTCOME_INDETERMINATE", "unknown")) : (HttpStatusCode.OK, """{ "metadata": {}, "invoices": [] }""");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_KeyReusedWithAnotherBody_IsARejectionOfTheProvider()
    {
        // Arrange
        alanube.Answer = _ => ((HttpStatusCode)422, AlanubeAnswers.Errors("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "This Idempotency-Key was already used with a different request payload."));

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Provider, "422,IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD"), (submission.State.Status, submission.State.RejectionOrigin!.Value, submission.State.Raw));
    }

    [Fact]
    public async Task SubmitAsync_RefusalOfTheContent_IsARejectionOfTheProviderWithTheMessages()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.BadRequest, AlanubeAnswers.SchemaRefusal);

        // Act
        var submission = await SubmitAsync();

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, RejectionOrigin.Provider, "instance requires property \"documentType\""), (submission.State.Status, submission.State.RejectionOrigin!.Value, submission.State.Reasons[0]));
    }

    [Fact]
    public async Task SubmitAsync_TokenRejected_ThrowsProviderCredentialsRejected()
    {
        // Arrange: what the sandbox answers to an invalid token.
        alanube.Answer = _ => (HttpStatusCode.Forbidden, """{"message":"Forbidden"}""");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderCredentialsRejected.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_TaxAuthorityCommunicationError_ThrowsProviderUnavailable()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.ServiceUnavailable, AlanubeAnswers.Errors("EPR500", "Communication Error"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync());

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_IssuerNotCreatedInAlanube_ThrowsProviderIssuerNotReady()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync(issuer: Issuer(companyId: null)));

        // Assert
        Assert.Equal((InfraErrors.ProviderIssuerNotReady.GetCode(), 0), (exception.Code, alanube.Requests.Count));
    }

    [Fact]
    public async Task SubmitAsync_InvoiceWithoutTechnicalKey_IsRejectedWithoutCallingAlanube()
    {
        // Act
        var submission = await SubmitAsync(Document(range: Range(technicalKey: null)));

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, 0), (submission.State.Status, alanube.Requests.Count));
    }

    [Fact]
    public async Task SubmitAsync_WithoutToken_ThrowsPlatformTokenNotConfigured()
    {
        // Arrange
        var options = FakeAlanube.Options();

        options.Master.Token = null;

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => SubmitAsync(options: options));

        // Assert
        Assert.Equal(InfraErrors.PlatformTokenNotConfigured.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SubmitAsync_SameDocumentTwice_SendsTheSameIdempotencyKey()
    {
        // Arrange
        var document = Document();

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(document);
        await SubmitAsync(document);

        // Assert
        Assert.Single(alanube.Calls.Select(x => x.IdempotencyKey).Distinct());
    }

    [Fact]
    public async Task SubmitAsync_CorrectedWithTheSameContent_SendsAnotherIdempotencyKey()
    {
        // Arrange
        var id = Guid.NewGuid();

        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(id: id));
        await SubmitAsync(Document(id: id, revision: 1));

        // Assert
        Assert.Equal(2, alanube.Calls.Select(x => x.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public void IdempotencyKeyOf_AnyDocument_IsAUuidOfVersion4()
    {
        // Act
        var key = AlanubeMapper.IdempotencyKeyOf(Guid.NewGuid(), 0, "{}");

        // Assert
        Assert.Matches(new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"), key);
    }

    [Fact]
    public async Task GetStateAsync_ById_ReadsTheDocument()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Accepted);

        // Act
        var state = await alanube.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01M717Z8XMRHR2S9Z5Y55NFG8X", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, "/e-provider/col/v1/invoices/01M717Z8XMRHR2S9Z5Y55NFG8X"), (state!.Status, alanube.Calls.Single().PathAndQuery));
    }

    [Fact]
    public async Task GetStateAsync_UnknownId_LooksItUpByItsNumber()
    {
        // Arrange
        alanube.Answer = request => request.RequestUri!.Query.Length == 0 ? (HttpStatusCode.NotFound, AlanubeAnswers.Errors("AEP6001", "Invoice not found")) : (HttpStatusCode.OK, AlanubeAnswers.List(AlanubeAnswers.Accepted));

        // Act
        var state = await alanube.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01ZZZZZZZZZZZZZZZZZZZZZZZZ", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(("01M717Z8XMRHR2S9Z5Y55NFG8X", ProviderDocumentStatus.Accepted), (state!.ProviderDocumentId, state.Status));
    }

    [Fact]
    public async Task GetStateAsync_NotInAlanube_ReturnsNull()
    {
        // Arrange
        alanube.Answer = request => request.RequestUri!.Query.Length == 0 ? (HttpStatusCode.NotFound, AlanubeAnswers.Errors("AEP6001", "Invoice not found")) : (HttpStatusCode.OK, """{ "metadata": {}, "invoices": [] }""");

        // Act
        var state = await alanube.Provider().GetStateAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01ZZZZZZZZZZZZZZZZZZZZZZZZ", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetFilesAsync_Accepted_DownloadsTheXmlTheAttachedDocumentAndThePdfOfThePackage()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Accepted);
        alanube.Files["/files/fv.xml"] = Encoding.UTF8.GetBytes("<Invoice/>");
        alanube.Files["/files/ad.xml"] = Encoding.UTF8.GetBytes("<AttachedDocument/>");
        alanube.Files["/files/package.zip"] = FakeAlanube.Zip(("ad09005590880852600003E18.pdf", "%PDF-1.4"), ("ad09005590880852600003E18.xml", "<AttachedDocument/>"));

        // Act
        var files = await alanube.Provider().GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01M717Z8XMRHR2S9Z5Y55NFG8X", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal(("SETP990641077.xml", "<Invoice/>", "SETP990641077-attached.xml", "SETP990641077.pdf", "%PDF-1.4"), (files.Xml!.Name, Encoding.UTF8.GetString(files.Xml.Content), files.AttachedDocument!.Name, files.Pdf!.Name, Encoding.UTF8.GetString(files.Pdf.Content)));
    }

    [Fact]
    public async Task GetFilesAsync_AnyLink_GoesWithoutTheToken()
    {
        // Arrange: the signed link is the authorization, and the storage refuses a foreign one.
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Accepted);

        // Act
        await alanube.Provider().GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01M717Z8XMRHR2S9Z5Y55NFG8X", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal((3, true), (alanube.Downloads.Count(), alanube.Downloads.All(x => x.Authorization is null)));
    }

    [Fact]
    public async Task GetFilesAsync_AttachedDocumentLinkFails_TakesItFromThePackage()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Accepted);
        alanube.Files["/files/package.zip"] = FakeAlanube.Zip(("ad1.pdf", "%PDF-1.4"), ("ad1.xml", "<AttachedDocument/>"));

        // Act
        var files = await alanube.Provider().GetFilesAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "01M717Z8XMRHR2S9Z5Y55NFG8X", Number, "key"), CancellationToken.None);

        // Assert
        Assert.Equal((true, "<AttachedDocument/>"), (files.Xml is null, Encoding.UTF8.GetString(files.AttachedDocument!.Content)));
    }

    [Fact]
    public async Task ProvisionAsync_NewCompany_CreatesItWithTheCertificateOfAlegra()
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.NoCompanies) : (HttpStatusCode.Created, AlanubeAnswers.Company);

        // Act
        var provisioned = await alanube.Provider().ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        var body = alanube.LastBody;

        Assert.Equal((FakeAlanube.CompanyId, "Emisor de Prueba SAS", "900000002", "1", "true", "1", "31", "O-13", "false"), (provisioned.ProviderIssuerId, Text(body, "name"), Text(body, "identification"), Text(body, "dv"), Text(body, "useAlegraCertificate"), Text(body, "organizationType"), Text(body, "identificationType"), Text(body, "regimeCode"), Text(body, "notificationByEmail", "enabled")));
    }

    [Fact]
    public async Task ProvisionAsync_CompanyAlreadyThere_AdoptsTheAssociatedOneWithoutCreatingAnother()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Companies);

        // Act
        var provisioned = await alanube.Provider().ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        Assert.Equal((FakeAlanube.CompanyId, 0), (provisioned.ProviderIssuerId, alanube.Calls.Count(x => x.Method == HttpMethod.Post)));
    }

    [Fact]
    public async Task ProvisionAsync_WebhooksEnabled_RegistersThemWithTheSecretHeader()
    {
        // Arrange
        var options = FakeAlanube.Options();

        options.Webhooks = new AlanubeWebhooksOptions { Enabled = true, Url = new Uri("https://kappali.example.test/ms-electronicinvoicing/api/ProviderEvents/Alanube"), Header = "x-api-key", Secret = "invented-secret" };
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.NoCompanies) : (HttpStatusCode.Created, AlanubeAnswers.Company);

        // Act
        await alanube.Provider(options).ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        var hook = alanube.LastBody.GetProperty("webhooks").GetProperty("equivalentDocuments").GetProperty("emissionFinished");

        Assert.Equal(("https://kappali.example.test/ms-electronicinvoicing/api/ProviderEvents/Alanube", "invented-secret", "active"), (Text(hook, "url"), Text(hook, "headers", "x-api-key"), Text(hook, "status")));
    }

    [Fact]
    public async Task ProvisionAsync_RefusedByAlanube_ThrowsProviderIssuerNotReady()
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.NoCompanies) : (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP17001", "The NIT of the company is invalid"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider().ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.ProviderIssuerNotReady.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData(ElectronicDocumentType.Invoice, "invoices")]
    [InlineData(ElectronicDocumentType.PosTicket, "pos")]
    public async Task RunTestSetAsync_TypeOfDocument_SendsItsTestSetForTheCompany(ElectronicDocumentType type, string kind)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, $$"""{ "testSets": [ { "id": "1", "type": "{{kind}}", "governmentId": "a70562e0-631e-4ceb-aa65-36887b57dc17", "status": "ACCEPTED" } ] }""");

        // Act
        var testSet = await alanube.Provider().RunTestSetAsync(Issuer(), type, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Accepted, "/e-provider/col/v1/test-sets", kind, "a70562e0-631e-4ceb-aa65-36887b57dc17", FakeAlanube.CompanyId), (testSet.Status, alanube.Calls.Single().PathAndQuery, Text(alanube.LastBody, "type"), Text(alanube.LastBody, "governmentId"), Text(alanube.LastBody, "company", "id")));
    }

    [Theory]
    [InlineData("REJECTED", ProviderDocumentStatus.Rejected)]
    [InlineData("WAITING_RESPONSE", ProviderDocumentStatus.Processing)]
    public async Task RunTestSetAsync_StatusOfTheSet_IsNormalized(string status, ProviderDocumentStatus expected)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, $$"""{ "testSets": [ { "type": "invoices", "status": "{{status}}" } ] }""");

        // Act
        var testSet = await alanube.Provider().RunTestSetAsync(Issuer(), ElectronicDocumentType.Invoice, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None);

        // Assert
        Assert.Equal(expected, testSet.Status);
    }

    [Fact]
    public async Task RunTestSetAsync_SetAlreadyApproved_IsAccepted()
    {
        // Arrange: what the sandbox answers to a company already enabled.
        alanube.Answer = _ => (HttpStatusCode.BadRequest, """{"errors":[{"code":"AEP4007","message":"Test set in this company has already been approved. No additional submissions are required.","httpCode":400}],"approvedTestSet":{"id":"01M7177CXYYYJ7Y1X6443EXY06","governmentId":"a70562e0-631e-4ceb-aa65-36887b57dc17","type":"invoices","status":"ACCEPTED","errors":[]}}""");

        // Act
        var testSet = await alanube.Provider().RunTestSetAsync(Issuer(), ElectronicDocumentType.Invoice, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None);

        // Assert
        Assert.Equal(ProviderDocumentStatus.Accepted, testSet.Status);
    }

    [Fact]
    public async Task RunTestSetAsync_RefusedByAlanube_IsRejected()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP1006", "The identification and dv of the company is required for send set test"));

        // Act
        var testSet = await alanube.Provider().RunTestSetAsync(Issuer(), ElectronicDocumentType.Invoice, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.Rejected, "400,AEP1006"), (testSet.Status, testSet.Raw));
    }

    [Fact]
    public async Task GetNumberingRangesAsync_Production_ReadsTheResolutionsOfTheTaxId()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, """{ "resolutions": [ { "resolutionNumber": "18764000000001", "prefix": "FE", "minNumber": 1, "maxNumber": 5000, "startDate": "2026-11-01", "endDate": "2028-11-01", "technicalKey": "technical-key-of-test" } ] }""");

        // Act
        var ranges = await alanube.Provider().GetNumberingRangesAsync(Issuer(), CancellationToken.None);

        // Assert
        Assert.Equal((new ProviderNumberingRange("technical-key-of-test", "FE", "18764000000001", 1, 5000, new LocalDate(2026, 11, 1), new LocalDate(2028, 11, 1)), "/e-provider/col/v1/resolutions/900000002"), (ranges.Single(), alanube.Calls.Single().PathAndQuery));
    }

    [Fact]
    public async Task GetNumberingRangesAsync_Sandbox_ReturnsNone()
    {
        // Arrange: what the sandbox answers.
        alanube.Answer = _ => (HttpStatusCode.NotFound, AlanubeAnswers.Errors("AEP9006", "Environment not supported. Production environment only"));

        // Act
        var ranges = await alanube.Provider().GetNumberingRangesAsync(Issuer(), CancellationToken.None);

        // Assert
        Assert.Empty(ranges);
    }

    [Fact]
    public async Task SubmitAsync_DebitNote_RefersToTheInvoiceItCorrectsWithItsConceptAndWithoutAResolution()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, AlanubeAnswers.Accepted);

        // Act
        await SubmitAsync(Document(ElectronicDocumentType.DebitNote));

        // Assert: the note of debit has no type of document of its own in Alanube, and no note carries a resolution.
        var body = alanube.LastBody;
        var reference = body.GetProperty("associatedDocuments")[0];

        Assert.Equal(("/e-provider/col/v1/debit-notes", "SETP", "990641077", "2", "01", "990000001", "SETP", "cufe-of-the-corrected-invoice", false, false), (alanube.Calls.Single().PathAndQuery, Text(body, "prefix"), Text(body, "number"), Text(body, "conceptCode"), Text(reference, "documentType"), Text(reference, "number"), Text(reference, "prefix"), Text(reference, "uuid"), body.TryGetProperty("resolution", out _), body.TryGetProperty("documentType", out _)));
    }

    [Theory]
    [InlineData("invoices")]
    [InlineData("creditNotes")]
    [InlineData("debitNotes")]
    [InlineData("equivalentDocuments")]
    public async Task ProvisionAsync_WebhooksEnabled_RegistersThemForEveryKindOfDocument(string collection)
    {
        // Arrange
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.NoCompanies) : (HttpStatusCode.Created, AlanubeAnswers.Company);

        // Act
        await alanube.Provider(WithWebhooks()).ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        var hook = alanube.LastBody.GetProperty("webhooks").GetProperty(collection).GetProperty("emissionFinished");

        Assert.Equal(("https://kappali.example.test/hooks", "invented-secret", "active"), (Text(hook, "url"), Text(hook, "headers", "x-api-key"), Text(hook, "status")));
    }

    [Fact]
    public async Task ProvisionAsync_CompanyAlreadyThereWithWebhooksOn_RegistersThemInTheAdoptedCompany()
    {
        // Arrange: a company created by hand, or before the webhooks were on, would never call.
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.Companies) : (HttpStatusCode.OK, AlanubeAnswers.Company);

        // Act
        await alanube.Provider(WithWebhooks()).ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        var patch = alanube.Calls.Single(x => x.Method == HttpMethod.Patch);
        var hook = alanube.LastBody.GetProperty("webhooks").GetProperty("invoices").GetProperty("emissionFinished");

        Assert.Equal(($"/e-provider/col/v1/companies/{FakeAlanube.CompanyId}", "invented-secret", 0), (patch.PathAndQuery, Text(hook, "headers", "x-api-key"), alanube.Calls.Count(x => x.Method == HttpMethod.Post)));
    }

    [Fact]
    public async Task ProvisionAsync_CompanyAlreadyThereWithWebhooksOff_ChangesNothingInIt()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Companies);

        // Act
        await alanube.Provider().ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        Assert.DoesNotContain(alanube.Calls, x => x.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ProvisionAsync_AdoptedCompanyRefusesTheWebhooks_StillAdoptsIt()
    {
        // Arrange: the status job asks for every result anyway.
        alanube.Answer = request => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, AlanubeAnswers.Companies) : (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP17002", "Invalid webhook url"));

        // Act
        var provisioned = await alanube.Provider(WithWebhooks()).ProvisionAsync(Issuer(companyId: null), new IssuerContact("Ana", "Prueba", "ana@example.com", "3000000002"), CancellationToken.None);

        // Assert
        Assert.Equal(FakeAlanube.CompanyId, provisioned.ProviderIssuerId);
    }

    /// <summary>
    /// Las Acacias was adopted in the sandbox before the webhooks were turned on: the round registers them in its company
    /// (Kappali pendings/744, §15).
    /// </summary>
    [Fact]
    public async Task RegisterWebhooksAsync_CompanyOfTheIssuer_PatchesItWithTheWebhooks()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Company);

        // Act
        await alanube.Provider(WithWebhooks()).RegisterWebhooksAsync(Issuer(), CancellationToken.None);

        // Assert
        var patch = alanube.Calls.Single();
        var hook = alanube.LastBody.GetProperty("webhooks").GetProperty("equivalentDocuments").GetProperty("emissionFinished");

        Assert.Equal((HttpMethod.Patch, $"/e-provider/col/v1/companies/{FakeAlanube.CompanyId}", "https://kappali.example.test/hooks", "invented-secret"), (patch.Method, patch.PathAndQuery, Text(hook, "url"), Text(hook, "headers", "x-api-key")));
    }

    [Fact]
    public async Task RegisterWebhooksAsync_AlanubeRefusesThem_ThrowsProviderWebhooksRejected()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP17002", "Invalid webhook url"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider(WithWebhooks()).RegisterWebhooksAsync(Issuer(), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.ProviderWebhooksRejected.GetCode(), exception.Code);
    }

    [Fact]
    public async Task RegisterWebhooksAsync_IssuerNotCreatedInAlanube_ThrowsProviderIssuerNotReady()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider(WithWebhooks()).RegisterWebhooksAsync(Issuer(companyId: null), CancellationToken.None));

        // Assert
        Assert.Equal((InfraErrors.ProviderIssuerNotReady.GetCode(), 0), (exception.Code, alanube.Calls.Count()));
    }

    [Fact]
    public void WebhooksFingerprint_WebhooksOff_IsNull()
    {
        // Act
        var fingerprint = alanube.Provider().WebhooksFingerprint;

        // Assert
        Assert.Null(fingerprint);
    }

    [Fact]
    public void WebhooksFingerprint_SecretChanged_ChangesAndDoesNotCarryTheSecret()
    {
        // Arrange
        var rotated = WithWebhooks();
        rotated.Webhooks.Secret = "another-invented-secret";

        // Act
        var before = alanube.Provider(WithWebhooks()).WebhooksFingerprint;
        var after = alanube.Provider(rotated).WebhooksFingerprint;

        // Assert
        Assert.NotEqual(before, after);
        Assert.DoesNotContain("invented-secret", before!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ElectronicDocumentType.CreditNote, "invoices")]
    [InlineData(ElectronicDocumentType.DebitNote, "invoices")]
    [InlineData(ElectronicDocumentType.PosAdjustmentNote, "pos")]
    public async Task RunTestSetAsync_TypeOfANote_SendsTheTestSetOfTheDocumentItCorrects(ElectronicDocumentType type, string kind)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.Created, $$"""{ "testSets": [ { "type": "{{kind}}", "status": "ACCEPTED" } ] }""");

        // Act
        await alanube.Provider().RunTestSetAsync(Issuer(), type, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None);

        // Assert
        Assert.Equal(kind, Text(alanube.LastBody, "type"));
    }

    [Fact]
    public async Task RunTestSetAsync_IssuerNotCreatedInAlanube_ThrowsProviderIssuerNotReady()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider().RunTestSetAsync(Issuer(companyId: null), ElectronicDocumentType.Invoice, "a70562e0-631e-4ceb-aa65-36887b57dc17", CancellationToken.None));

        // Assert
        Assert.Equal((InfraErrors.ProviderIssuerNotReady.GetCode(), 0), (exception.Code, alanube.Calls.Count()));
    }

    [Theory]
    [InlineData("AEP9008", "The NIT does not belong to any company of the user")]
    [InlineData("AEP9009", "The company has not passed the enablement")]
    public async Task GetNumberingRangesAsync_CompanyNotOfTheUserOrNotEnabled_ReturnsNone(string code, string message)
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.NotFound, AlanubeAnswers.Errors(code, message));

        // Act
        var ranges = await alanube.Provider().GetNumberingRangesAsync(Issuer(), CancellationToken.None);

        // Assert
        Assert.Empty(ranges);
    }

    [Fact]
    public async Task GetNumberingRangesAsync_ResolutionWithoutTechnicalKey_KeepsItWithoutKey()
    {
        // Arrange: the numbering of the point of sale has no technical key.
        alanube.Answer = _ => (HttpStatusCode.OK, """{ "resolutions": [ { "resolutionNumber": "18764000000002", "prefix": "POS", "minNumber": 1, "maxNumber": 1000, "startDate": "2026-11-01", "endDate": "2028-11-01" } ] }""");

        // Act
        var ranges = await alanube.Provider().GetNumberingRangesAsync(Issuer(), CancellationToken.None);

        // Assert
        Assert.Equal((null, "POS"), (ranges.Single().TechnicalKey, ranges.Single().Prefix));
    }

    private static AlanubeOptions WithWebhooks()
    {
        var options = FakeAlanube.Options();

        options.Webhooks = new AlanubeWebhooksOptions { Enabled = true, Url = new Uri("https://kappali.example.test/hooks"), Header = "x-api-key", Secret = "invented-secret" };

        return options;
    }

    [Fact]
    public void IsAuthentic_TheSecretInItsHeader_IsTrue()
    {
        // Act & Assert
        Assert.True(alanube.Provider(WithWebhooks()).IsAuthentic("{}", new Dictionary<string, string> { ["X-Api-Key"] = "invented-secret" }));
    }

    [Theory]
    [InlineData("x-api-key", "another-secret")]
    [InlineData("x-api-key", "")]
    [InlineData("x-other", "invented-secret")]
    public void IsAuthentic_AnotherSecretOrHeader_IsFalse(string header, string value)
    {
        // Act & Assert
        Assert.False(alanube.Provider(WithWebhooks()).IsAuthentic("{}", new Dictionary<string, string> { [header] = value }));
    }

    [Fact]
    public void IsAuthentic_WebhooksOff_IsFalse()
    {
        // Act & Assert
        Assert.False(alanube.Provider().IsAuthentic("{}", new Dictionary<string, string> { ["x-api-key"] = "invented-secret" }));
    }

    [Fact]
    public async Task ReadAsync_EmissionFinished_NamesTheDocument()
    {
        // Arrange: the body of invoices.emissionFinished in docs/alanube/docs/webhook-invoicesemissionfinished.md.
        const string body = """{ "invoice": { "type": "invoice", "id": "01FQS9ZGW84DG8C2ZFDZJZGEV2", "status": "SENT", "legalStatus": "ACCEPTED", "errors": [] } }""";

        // Act
        var read = await alanube.Provider().ReadAsync(body, new Dictionary<string, string>(), CancellationToken.None);

        // Assert
        Assert.Equal(new ProviderDocumentEvent("01FQS9ZGW84DG8C2ZFDZJZGEV2", "ACCEPTED"), read);
    }

    [Fact]
    public async Task ReadAsync_GovernmentStatusChanged_IsNotAboutADocument()
    {
        // Act
        var read = await alanube.Provider().ReadAsync("""{ "company": { "id": "xGFxsdqw123", "governmentStatus": { "payrolls": "AUTHORIZED" } } }""", new Dictionary<string, string>(), CancellationToken.None);

        // Assert
        Assert.Null(read);
    }

    [Fact]
    public void Capabilities_Default_AreThoseOfAMainAccountThatNumbersHereWithAnIdempotencyKey()
    {
        // Act
        var capabilities = alanube.Provider().Capabilities;

        // Assert
        Assert.Equal((NumberingMode.ByPlatform, true, IdempotencyMode.Header, StatusUpdatesMode.Poll, true), (capabilities.Numbering, capabilities.HabilitationByApi, capabilities.Idempotency, capabilities.StatusUpdates, capabilities.DocumentTypes.Contains(ElectronicDocumentType.PosAdjustmentNote)));
    }

    [Fact]
    public void Capabilities_WebhooksOn_ReceivesTheResultsByCalls()
    {
        // Act & Assert
        Assert.Equal(StatusUpdatesMode.Push, alanube.Provider(WithWebhooks()).Capabilities.StatusUpdates);
    }

    [Fact]
    public void Validate_WithoutTheTypesOfTheDocuments_SaysWhatIsMissing()
    {
        // Arrange
        var options = FakeAlanube.Options();

        options.Documents.Clear();

        // Act
        var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        // Assert
        Assert.Equal(4, problems.Count);
    }

    /// <summary>
    /// The copropiedad declared itself responsible for VAT and its company in Alanube still said otherwise (Kappali
    /// pendings/752): the company takes the regime and the responsibilities, and nothing that restarts its enablement.
    /// </summary>
    [Fact]
    public async Task UpdateIssuerAsync_CompanyOfTheIssuer_PatchesItsRegimeAndResponsibilitiesOnly()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.OK, AlanubeAnswers.Company);
        var issuer = Issuer() with { TaxRegimeCode = "48", TaxResponsibilities = ["O-13", "O-23"] };

        // Act
        await alanube.Provider().UpdateIssuerAsync(issuer, CancellationToken.None);

        // Assert
        var patch = alanube.Calls.Single();
        var body = alanube.LastBody;

        Assert.Equal((HttpMethod.Patch, $"/e-provider/col/v1/companies/{FakeAlanube.CompanyId}", "O-13;O-23", "01", false, false, false), (patch.Method, patch.PathAndQuery, Text(body, "regimeCode"), Text(body, "taxCode", "id"), body.TryGetProperty("identification", out _), body.TryGetProperty("name", out _), body.TryGetProperty("webhooks", out _)));
    }

    [Fact]
    public async Task UpdateIssuerAsync_AlanubeRefusesTheData_ThrowsProviderIssuerUpdateRejected()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.BadRequest, AlanubeAnswers.Errors("AEP17001", "Invalid regimeCode"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider().UpdateIssuerAsync(Issuer(), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.ProviderIssuerUpdateRejected.GetCode(), exception.Code);
    }

    [Fact]
    public async Task UpdateIssuerAsync_AlanubeDoesNotAnswer_ThrowsProviderUnavailable()
    {
        // Arrange
        alanube.Answer = _ => (HttpStatusCode.ServiceUnavailable, "{}");

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider().UpdateIssuerAsync(Issuer(), CancellationToken.None));

        // Assert
        Assert.Equal(InfraErrors.ProviderUnavailable.GetCode(), exception.Code);
    }

    [Fact]
    public async Task UpdateIssuerAsync_IssuerNotCreatedInAlanube_ThrowsProviderIssuerNotReadyWithoutCalling()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => alanube.Provider().UpdateIssuerAsync(Issuer(companyId: null), CancellationToken.None));

        // Assert
        Assert.Equal((InfraErrors.ProviderIssuerNotReady.GetCode(), 0), (exception.Code, alanube.Calls.Count()));
    }
}
