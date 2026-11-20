using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Simulated;

/// <summary>
/// The simulated provider writes the email that delivers a validated document to its buyer, saying it is a test; without
/// an email of the buyer, or told not to send, it writes none.
/// </summary>
public class SimulatedBuyerNoticeTest
{
    private static readonly Guid DocumentId = Guid.Parse("bb000000-0000-4000-8000-000000000001");

    private static SimulatedProvider Provider(SimulatedOptions? options = null) => new(Microsoft.Extensions.Options.Options.Create(options ?? new SimulatedOptions()));

    private static ProviderIssuer Issuer() => ProviderIssuer.From(TestData.EnabledIssuer(), new Dictionary<string, string>());

    private static async Task<BuyerNotice?> NoticeOf(DocumentContent content, SimulatedOptions? options = null)
    {
        var provider = Provider(options);
        var submission = await provider.SubmitAsync(Issuer(), new ProviderDocument(DocumentId, ElectronicDocumentType.Invoice, new DocumentNumber("SETP", 990000001), null, content, "key", new LocalDate(2026, 11, 10)), CancellationToken.None);

        return await provider.GetBuyerNoticeAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, submission.ProviderDocumentId, null, "key"), CancellationToken.None);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_BuyerWithEmail_GoesToThatEmail()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent());

        // Assert
        Assert.Equal("comprador@example.com", notice!.To);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_BuyerWithEmail_SaysInTheSubjectThatItIsATest()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent());

        // Assert
        Assert.Equal("Documento de prueba sin validez fiscal: Factura electrónica de venta SETP990000001", notice!.Subject);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_BuyerWithEmail_SaysInTheBodyWhatItDeliversAndThatItIsATest()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent());

        // Assert: the texts go encoded for the HTML of the template.
        Assert.Equal((true, true, true, true), (
            notice!.Content.Contains("<p>Hola, Comprador de Prueba SAS:</p>", StringComparison.Ordinal),
            notice.Content.Contains(WebUtility.HtmlEncode("Emisor de Prueba SAS te envía el documento «Factura electrónica de venta» número SETP990000001, por un total de COP 23.800,00."), StringComparison.Ordinal),
            notice.Content.Contains(WebUtility.HtmlEncode("Este es un documento de prueba sin validez fiscal"), StringComparison.Ordinal),
            notice.Title == "Documento de prueba SETP990000001"));
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_NamesWithMarkup_AreEncodedInTheBody()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { Name = "Pérez & Hijos <b>SAS</b>" } };

        // Act
        var notice = await NoticeOf(content);

        // Assert
        Assert.Equal((false, true), (notice!.Content.Contains("<b>SAS</b>", StringComparison.Ordinal), notice.Content.Contains("P&#233;rez &amp; Hijos &lt;b&gt;SAS&lt;/b&gt;", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetBuyerNoticeAsync_BuyerWithoutEmail_WritesNone(string? email)
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { Email = email } });

        // Assert
        Assert.Null(notice);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_ToldNotToSend_WritesNone()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent(), new SimulatedOptions { NotifyBuyerByEmail = false });

        // Assert
        Assert.Null(notice);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_RejectedDocument_WritesNone()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent("222222222"), new SimulatedOptions { Rules = [new SimulatedRule { BuyerIdentification = "222222222", Outcome = SimulatedOutcome.Rejected }] });

        // Assert
        Assert.Null(notice);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_DocumentStillWaiting_WritesNone()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent("555555555"), new SimulatedOptions { Rules = [new SimulatedRule { BuyerIdentification = "555555555", Outcome = SimulatedOutcome.Processing, ChecksUntilResolved = 3 }] });

        // Assert
        Assert.Null(notice);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_DocumentItNeverReceived_WritesNone()
    {
        // Act
        var notice = await Provider().GetBuyerNoticeAsync(Issuer(), new ProviderDocumentLookup(ElectronicDocumentType.Invoice, "unknown", null, "key"), CancellationToken.None);

        // Assert
        Assert.Null(notice);
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_AnotherLanguage_WritesItInThatLanguage()
    {
        // Act
        var notice = await NoticeOf(TestData.InvoiceContent(), new SimulatedOptions { Language = "en" });

        // Assert
        Assert.Equal("Test document with no fiscal validity: Electronic sales invoice SETP990000001", notice!.Subject);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capabilities_DeliversToBuyer_FollowsTheSetting(bool notify)
    {
        // Act
        var capabilities = Provider(new SimulatedOptions { NotifyBuyerByEmail = notify }).Capabilities;

        // Assert
        Assert.Equal(notify, capabilities.DeliversToBuyer);
    }

    [Fact]
    public void Options_ByDefault_SendTheEmail()
    {
        // Act
        var options = new SimulatedOptions();

        // Assert
        Assert.True(options.NotifyBuyerByEmail);
    }
}
