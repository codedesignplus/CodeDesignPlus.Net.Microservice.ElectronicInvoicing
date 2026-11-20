using System.Globalization;
using System.Text;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using Microsoft.Extensions.DependencyInjection;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Simulated;

/// <summary>
/// The graphic representation the simulated provider hands over is a PDF a reader accepts, with what the document says
/// and the mark that it is a test.
/// </summary>
public class SimulatedGraphicRepresentationTest
{
    private const string Mark = "DOCUMENTO DE PRUEBA — SIN VALIDEZ FISCAL";

    private static readonly Guid DocumentId = Guid.Parse("bb000000-0000-4000-8000-000000000001");

    private static SimulatedProvider Provider(string language = "es") => new(Microsoft.Extensions.Options.Options.Create(new SimulatedOptions { Language = language }));

    private static ProviderIssuer Issuer() => ProviderIssuer.From(TestData.EnabledIssuer(), new Dictionary<string, string>());

    private static async Task<byte[]> PdfOf(ElectronicDocumentType type, DocumentContent content, string language = "es", ProviderNumberingRange? range = null)
    {
        var provider = Provider(language);
        var submission = await provider.SubmitAsync(Issuer(), new ProviderDocument(DocumentId, type, new DocumentNumber("SETP", 990000001), range, content, "key", new LocalDate(2026, 11, 10)), CancellationToken.None);
        var files = await provider.GetFilesAsync(Issuer(), new ProviderDocumentLookup(type, submission.ProviderDocumentId, null, "key"), CancellationToken.None);

        return files.Pdf!.Content;
    }

    private static Task<byte[]> InvoicePdf(string language = "es") => PdfOf(ElectronicDocumentType.Invoice, TestData.InvoiceContent(), language);

    /// <summary>
    /// Whether the file has a text, as it is written in it: in the WinAnsi encoding and with its parentheses escaped.
    /// </summary>
    private static bool Has(byte[] pdf, string text) => pdf.AsSpan().IndexOf(PlainPdf.Encode(text.Replace("(", @"\(", StringComparison.Ordinal).Replace(")", @"\)", StringComparison.Ordinal))) >= 0;

    [Fact]
    public async Task GetFilesAsync_Invoice_HandsOverAFileThatBeginsAndEndsAsAPdf()
    {
        // Act
        var pdf = await InvoicePdf();

        // Assert
        Assert.Equal(("%PDF-1.4\n", "%%EOF\n"), (Encoding.ASCII.GetString(pdf, 0, 9), Encoding.ASCII.GetString(pdf, pdf.Length - 6, 6)));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_HasACrossReferenceThatPointsAtEveryObject()
    {
        // Act
        var pdf = await InvoicePdf();

        // Assert
        Assert.Empty(PdfStructure.BrokenReferences(pdf));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_DeclaresTheLengthOfEveryStream()
    {
        // Act
        var pdf = await InvoicePdf();

        // Assert
        Assert.Equal((true, 0), (PdfStructure.Streams(pdf).Count > 0, PdfStructure.Streams(pdf).Count(x => x.Declared != x.Actual)));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_HasOnePageUnderTheCatalog()
    {
        // Act
        var pdf = Encoding.Latin1.GetString(await InvoicePdf());

        // Assert
        Assert.Equal((true, true, true), (pdf.Contains("/Type /Catalog /Pages 2 0 R", StringComparison.Ordinal), pdf.Contains("/Type /Pages /Count 1 /Kids [6 0 R]", StringComparison.Ordinal), pdf.Contains("/Root 1 0 R", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_SaysItsNumberItsTotalAndThatItIsATest()
    {
        // Act
        var pdf = await InvoicePdf();

        // Assert: 2.380.000 minor units of a currency with two decimals.
        Assert.Equal((true, true, true), (Has(pdf, "SETP990000001"), Has(pdf, "COP 23.800,00"), Has(pdf, Mark)));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_SaysTheIssuerTheBuyerTheLineTheTaxAndTheCufe()
    {
        // Arrange
        var cufe = Convert.ToHexStringLower(System.Security.Cryptography.SHA384.HashData(Encoding.UTF8.GetBytes($"{DocumentId}|SETP990000001")));

        // Act
        var pdf = await InvoicePdf();

        // Assert
        Assert.Equal((true, true, true, true, true, true, true), (Has(pdf, "Emisor de Prueba SAS"), Has(pdf, "900000002-1"), Has(pdf, "Comprador de Prueba SAS"), Has(pdf, "900000001-7"), Has(pdf, "Servicio de prueba"), Has(pdf, "IVA (01) 19 %"), Has(pdf, cufe)));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_SaysTheDatesOfTheDocument()
    {
        // Act: a cash sale is due the day it is issued, which the simulation takes from the clock.
        var pdf = await InvoicePdf();
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["America/Bogota"]).Date.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture);

        // Assert
        Assert.True(Has(pdf, $"Fecha de emisión: {today}   Fecha de vencimiento: {today}"));
    }

    [Fact]
    public async Task GetFilesAsync_PosTicket_SaysTheDataOfItsRegister()
    {
        // Act
        var pdf = await PdfOf(ElectronicDocumentType.PosTicket, TestData.PosTicketContent());

        // Assert
        Assert.Equal((true, true, true, true), (Has(pdf, "DOCUMENTO EQUIVALENTE ELECTRÓNICO POS SETP990000001"), Has(pdf, "Caja: REG-001   Tipo de caja: Standard   Código de venta: SALE-0001"), Has(pdf, "Punto de venta: Punto de Prueba, Calle 9 # 8-7"), Has(pdf, "Cajero: Cajera de Prueba")));
    }

    [Fact]
    public async Task GetFilesAsync_Invoice_HasNoRegister()
    {
        // Act
        var pdf = await InvoicePdf();

        // Assert
        Assert.False(Has(pdf, "Tipo de caja"));
    }

    [Fact]
    public async Task GetFilesAsync_Note_SaysTheDocumentItCorrects()
    {
        // Act
        var pdf = await PdfOf(ElectronicDocumentType.CreditNote, TestData.NoteContent());

        // Assert
        Assert.Equal((true, true), (Has(pdf, "Documento que corrige: SETP990000001 (2026-11-01)   Concepto de corrección: 2"), Has(pdf, "cufe-of-the-corrected-invoice")));
    }

    [Fact]
    public async Task GetFilesAsync_DocumentNumberedByAResolution_SaysTheResolution()
    {
        // Act
        var pdf = await PdfOf(ElectronicDocumentType.Invoice, TestData.InvoiceContent(), range: new ProviderNumberingRange("key", "SETP", "18760000001", 990000000, 995000000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31)));

        // Assert: the sentence is longer than a row and continues in the next one.
        Assert.Equal((true, true), (Has(pdf, "Resolución 18760000001: prefijo SETP, del 990000000 al 995000000, vigente del 2026-01-01"), Has(pdf, "2027-12-31")));
    }

    [Fact]
    public async Task GetFilesAsync_TextsWithAccents_WritesThemInTheEncodingOfTheFonts()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { Name = "Begoña Núñez Peñalosa" }, Notes = ["Año 2026: administración — cuota ordinaria «única»"] };

        // Act
        var pdf = await PdfOf(ElectronicDocumentType.Invoice, content);

        // Assert: one byte per character, the one Windows-1252 gives it (ñ is 0xF1 and the dash 0x97).
        Assert.Equal((true, true, true), (pdf.AsSpan().IndexOf(Encoding.Latin1.GetBytes("Begoña Núñez Peñalosa")) >= 0, pdf.AsSpan().IndexOf(Encoding.Latin1.GetBytes("administración  cuota")) >= 0, Has(pdf, "«única»")));
    }

    [Fact]
    public async Task GetFilesAsync_TextWithParentheses_EscapesThemSoTheStringDoesNotEnd()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Notes = [@"Nota (con paréntesis) y \ barra"] };

        // Act
        var pdf = await PdfOf(ElectronicDocumentType.Invoice, content);

        // Assert
        Assert.True(pdf.AsSpan().IndexOf(PlainPdf.Encode(@"Nota \(con paréntesis\) y \\ barra")) >= 0);
    }

    [Fact]
    public async Task GetFilesAsync_CharacterTheEncodingLacks_WritesAQuestionMark()
    {
        // Arrange
        var content = TestData.InvoiceContent() with { Notes = ["Nota 日本 fin"] };

        // Act
        var pdf = await PdfOf(ElectronicDocumentType.Invoice, content);

        // Assert
        Assert.True(Has(pdf, "Nota ?? fin"));
    }

    [Fact]
    public async Task GetFilesAsync_MoreLinesThanAPageHolds_ContinuesOnAnotherPageWithTheMark()
    {
        // Arrange: eighty lines of 1.000,00 without taxes.
        var lines = Enumerable.Range(1, 80).Select(i => new DocumentLine($"SVC-{i}", $"Servicio {i}", 1, "94", 100_000, 0, 100_000, [], [])).ToList();
        var content = TestData.InvoiceContent() with { Lines = lines, Totals = new DocumentTotals("COP", 2, 8_000_000, 0, 0, 0, 0, 8_000_000) };

        // Act
        var pdf = await PdfOf(ElectronicDocumentType.Invoice, content);
        var text = Encoding.Latin1.GetString(pdf);

        // Assert
        Assert.Equal((true, 0, true, true), (text.Contains("/Count 2 /Kids [6 0 R 8 0 R]", StringComparison.Ordinal), PdfStructure.BrokenReferences(pdf).Count, Has(pdf, "Página 2 de 2"), Has(pdf, "Servicio 80")));
    }

    [Fact]
    public async Task GetFilesAsync_SameDocumentTwice_GivesTheSameBytes()
    {
        // Act
        var first = await InvoicePdf();
        var second = await InvoicePdf();

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetFilesAsync_AnotherLanguage_WritesItsTextsAndItsSeparators()
    {
        // Act
        var pdf = await InvoicePdf("en");

        // Assert
        Assert.Equal((true, true, false), (Has(pdf, "TEST DOCUMENT — NO FISCAL VALIDITY"), Has(pdf, "COP 23,800.00"), Has(pdf, Mark)));
    }

    [Fact]
    public async Task GetFilesAsync_CultureOfALanguage_UsesTheLanguage()
    {
        // Act
        var pdf = await InvoicePdf("es-CO");

        // Assert
        Assert.True(Has(pdf, Mark));
    }

    [Theory]
    [InlineData(0, "23.800")]
    [InlineData(2, "238,00")]
    [InlineData(3, "23,800")]
    public void Money_MinorUnits_AreWrittenWithTheDecimalsOfTheirCurrency(short digits, string expected)
    {
        // Arrange
        var totals = TestData.Totals() with { CurrencyDecimalDigits = digits };

        // Act
        var money = SimulatedGraphicRepresentation.Money(23_800, totals, SimulatedTexts.Find("es")!.NumberFormat());

        // Assert
        Assert.Equal(expected, money);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("fr")]
    public void Find_EveryLanguage_HasEveryText(string language)
    {
        // Act
        var texts = SimulatedTexts.Find(language)!;

        var empty = typeof(SimulatedTexts).GetProperties().Where(x => x.PropertyType == typeof(string) && string.IsNullOrEmpty((string?)x.GetValue(texts))).Select(x => x.Name).ToList();

        // Assert
        Assert.Empty(empty);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("fr")]
    public void Find_EveryLanguage_NamesEveryTypeOfDocument(string language)
    {
        // Act
        var texts = SimulatedTexts.Find(language)!;

        // Assert
        Assert.DoesNotContain(Enum.GetNames<ElectronicDocumentType>(), x => !texts.DocumentTypes.TryGetValue(x, out var name) || string.IsNullOrWhiteSpace(name));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("fr")]
    public void Find_EveryLanguage_FitsInTheEncodingOfTheFonts(string language)
    {
        // Act
        var texts = SimulatedTexts.Find(language)!;

        var written = typeof(SimulatedTexts).GetProperties().Where(x => x.PropertyType == typeof(string)).Select(x => (string)x.GetValue(texts)!).Concat(texts.DocumentTypes.Values);

        // Assert: no text of the language has a character the PDF would write as a question mark.
        Assert.DoesNotContain(written, x => !x.Contains('?') && PlainPdf.Encode(x).Contains((byte)'?'));
    }

    [Fact]
    public void Find_LanguageWithoutTexts_ReturnsNull()
    {
        // Act
        var texts = SimulatedTexts.Find("de");

        // Assert
        Assert.Null(texts);
    }

    [Fact]
    public void Initialize_LanguageWithoutTexts_StopsTheStart()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Simulated:Language"] = "de" }).Build();
        var services = new ServiceCollection();

        new Startup().Initialize(services, configuration);

        using var provider = services.BuildServiceProvider();

        // Act
        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SimulatedOptions>>().Value);

        // Assert
        Assert.Contains("Simulated:Language", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_TextLongerThanTheWidth_BreaksItAtItsSpaces()
    {
        // Act
        var lines = PlainPdf.Wrap("uno dos tres cuatro", 8);

        // Assert
        Assert.Equal(["uno dos", "tres", "cuatro"], lines);
    }

    [Fact]
    public void Wrap_WordLongerThanTheWidth_CutsIt()
    {
        // Act
        var lines = PlainPdf.Wrap("abcdefghij", 4);

        // Assert
        Assert.Equal(["abcd", "efgh", "ij"], lines);
    }
}

/// <summary>
/// Reads the structure of a PDF as a reader does: from the end, through the cross reference, to each object.
/// </summary>
internal static class PdfStructure
{
    /// <summary>
    /// The objects the cross reference does not point at: empty in a file a reader accepts.
    /// </summary>
    public static List<string> BrokenReferences(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var broken = new List<string>();

        var start = text.LastIndexOf("startxref\n", StringComparison.Ordinal);

        if (start < 0 || !text.EndsWith("\n%%EOF\n", StringComparison.Ordinal))
        {
            return ["startxref"];
        }

        var xref = int.Parse(text[(start + 10)..text.IndexOf('\n', start + 10)], CultureInfo.InvariantCulture);

        if (xref >= text.Length || !text[xref..].StartsWith("xref\n0 ", StringComparison.Ordinal))
        {
            return ["xref"];
        }

        var header = text.IndexOf('\n', xref + 5);
        var count = int.Parse(text[(xref + 7)..header], CultureInfo.InvariantCulture);
        var entries = header + 1;

        if (text.Substring(entries, 20) != "0000000000 65535 f \n")
        {
            broken.Add("free entry");
        }

        for (var number = 1; number < count; number++)
        {
            // Every entry takes twenty bytes: ten of offset, five of generation, the mark and the end of line.
            var entry = text.Substring(entries + (number * 20), 20);
            var offset = int.Parse(entry[..10], CultureInfo.InvariantCulture);

            if (!entry.EndsWith(" 00000 n \n", StringComparison.Ordinal) || offset >= text.Length || !text[offset..].StartsWith($"{number} 0 obj\n", StringComparison.Ordinal))
            {
                broken.Add($"object {number}");
            }
        }

        var trailer = text[(entries + (count * 20))..start];

        if (!trailer.StartsWith("trailer\n", StringComparison.Ordinal) || !trailer.Contains($"/Size {count} ", StringComparison.Ordinal))
        {
            broken.Add("trailer");
        }

        return broken;
    }

    /// <summary>
    /// The length each stream declares and the one it has.
    /// </summary>
    public static List<(int Declared, int Actual)> Streams(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var streams = new List<(int, int)>();
        var position = 0;

        while ((position = text.IndexOf("<< /Length ", position, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf(" >>\nstream\n", position, StringComparison.Ordinal);
            var declared = int.Parse(text[(position + 11)..end], CultureInfo.InvariantCulture);
            var begin = end + 11;

            streams.Add((declared, text.IndexOf("\nendstream\nendobj\n", begin, StringComparison.Ordinal) - begin));

            position = begin;
        }

        return streams;
    }
}
