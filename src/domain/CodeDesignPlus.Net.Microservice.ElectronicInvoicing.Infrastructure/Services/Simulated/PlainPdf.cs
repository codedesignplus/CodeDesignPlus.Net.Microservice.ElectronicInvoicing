using System.Globalization;
using System.Text;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// Writes a PDF of plain text, by hand: rows of a monospaced font on letter pages, each page with the mark of the test
/// document in a box, across the page and at its foot.
/// </summary>
/// <remarks>
/// <para>
/// By hand and not with a library: the simulated provider only needs pages of text, the two fonts it uses are among the
/// fourteen every PDF reader has (nothing is embedded) and a monospaced font aligns the columns by counting characters,
/// with no table of widths. A library would be a dependency of the service only to draw a test document.
/// </para>
/// <para>
/// The text is written in the WinAnsi encoding (Windows-1252), which has the accents of Spanish, Portuguese and French;
/// a character it lacks is written as a question mark. The streams are not compressed and nothing depends on the clock,
/// so the same document always gives the same bytes.
/// </para>
/// </remarks>
/// <param name="title">The title of the file, for the reader.</param>
/// <param name="mark">The mark of every page.</param>
/// <param name="watermark">The short mark written across every page.</param>
/// <param name="pageLabel">The page count, with <c>{page}</c> and <c>{pages}</c>.</param>
internal sealed class PlainPdf(string title, string mark, string watermark, string pageLabel)
{
    /// <summary>
    /// How many characters fit in a row.
    /// </summary>
    public const int Columns = 96;

    // The geometry of the page, in points: a letter page with its margins. It is the layout of this writer, not a
    // setting: the columns of the rows are counted from it.
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double Margin = 40;
    private const double FontSize = 9;
    private const double Leading = 11.5;
    private const double FirstRow = 716;
    private const int RowsPerPage = 58;

    // Every glyph of Courier advances 0.6 of the font size: it is what makes it monospaced.
    private const double Advance = 0.6;

    private readonly List<Row> rows = [];

    /// <summary>
    /// Adds a row of text. A text longer than a row continues in the next ones.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="bold">Whether it is written in bold.</param>
    public void Text(string? text, bool bold = false)
    {
        foreach (var line in Wrap(text ?? string.Empty, Columns))
        {
            rows.Add(new Row(line, bold, false));
        }
    }

    /// <summary>
    /// Adds a horizontal line.
    /// </summary>
    public void Rule() => rows.Add(new Row(string.Empty, false, true));

    /// <summary>
    /// Adds an empty row.
    /// </summary>
    public void Blank() => rows.Add(new Row(string.Empty, false, false));

    /// <summary>
    /// Breaks a text in lines of a width, at its spaces when it has them.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The characters of a line.</param>
    /// <returns>The lines; one empty line for an empty text.</returns>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var rest = Clean(text);

        while (rest.Length > width)
        {
            var cut = rest.LastIndexOf(' ', width);

            if (cut <= 0)
            {
                cut = width;
            }

            lines.Add(rest[..cut].TrimEnd());

            rest = rest[cut..].TrimStart();
        }

        lines.Add(rest);

        return lines;
    }

    /// <summary>
    /// The bytes of a text in the WinAnsi encoding.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>One byte per character; a question mark for the characters the encoding lacks.</returns>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];

        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = Encode(text[i]);
        }

        return bytes;
    }

    /// <summary>
    /// Builds the file.
    /// </summary>
    /// <returns>The bytes of the PDF.</returns>
    public byte[] Build()
    {
        List<Row[]> pages = rows.Count == 0 ? [[]] : [.. rows.Chunk(RowsPerPage)];

        // 1 catalog, 2 pages, 3 and 4 the fonts, 5 the information, and then a page and its content per page.
        const int FirstPageObject = 6;

        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{FirstPageObject + (i * 2)} 0 R"));
        byte[] information = [.. Ascii("<< /Title "), .. Literal(title), .. Ascii(" /Producer "), .. Literal(mark), .. Ascii(" >>")];

        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{kids}] >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"),
            information
        };

        for (var i = 0; i < pages.Count; i++)
        {
            var content = ContentOf(pages[i], i + 1, pages.Count);

            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {FirstPageObject + (i * 2) + 1} 0 R >>"));
            objects.Add([.. Ascii($"<< /Length {content.Length} >>\nstream\n"), .. content, .. Ascii("\nendstream")]);
        }

        using var file = new MemoryStream();

        // The second line, four bytes above 127, tells the tools that carry the file that it is binary.
        file.Write([.. Ascii("%PDF-1.4\n%"), 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var offsets = new List<long>();

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Position);

            file.Write(Ascii($"{i + 1} 0 obj\n"));
            file.Write(objects[i]);
            file.Write(Ascii("\nendobj\n"));
        }

        var xref = file.Position;

        file.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));

        foreach (var offset in offsets)
        {
            file.Write(Ascii($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n"));
        }

        file.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n"));

        return file.ToArray();
    }

    private byte[] ContentOf(Row[] page, int number, int count)
    {
        using var content = new MemoryStream();

        // Across the page, in light gray and under everything else: the short mark, as large as the diagonal lets it be.
        var watermarkSize = Math.Min(64, 690 / (Math.Max(1, watermark.Length) * Advance));
        var half = watermark.Length * Advance * watermarkSize / 2;
        const double Diagonal = 0.7071;
        var x = (PageWidth / 2) - (half * Diagonal) + (watermarkSize * 0.21);
        var y = (PageHeight / 2) - (half * Diagonal) - (watermarkSize * 0.21);

        content.Write(Ascii($"q 0.88 g BT /F2 {Number(watermarkSize)} Tf {Number(Diagonal)} {Number(Diagonal)} {Number(-Diagonal)} {Number(Diagonal)} {Number(x)} {Number(y)} Tm "));
        content.Write(Literal(watermark));
        content.Write(Ascii(" Tj ET Q\n"));

        // At the top, in a red box: the whole mark, centered.
        var width = PageWidth - (Margin * 2);
        var markSize = Math.Min(13, (width - 20) / (Math.Max(1, mark.Length) * Advance));
        var markX = Margin + ((width - (mark.Length * Advance * markSize)) / 2);

        content.Write(Ascii($"q 0.72 0.11 0.11 RG 1.5 w {Number(Margin)} 738 {Number(width)} 28 re S Q\n"));
        content.Write(Ascii($"BT 0.72 0.11 0.11 rg /F2 {Number(markSize)} Tf {Number(markX)} 747 Td "));
        content.Write(Literal(mark));
        content.Write(Ascii(" Tj ET\n0 g\n"));

        var row = FirstRow;

        foreach (var item in page)
        {
            if (item.IsRule)
            {
                content.Write(Ascii($"q 0.6 G 0.5 w {Number(Margin)} {Number(row + 3)} m {Number(PageWidth - Margin)} {Number(row + 3)} l S Q\n"));
            }
            else if (item.Text.Length > 0)
            {
                content.Write(Ascii($"BT /{(item.Bold ? "F2" : "F1")} {Number(FontSize)} Tf {Number(Margin)} {Number(row)} Td "));
                content.Write(Literal(item.Text));
                content.Write(Ascii(" Tj ET\n"));
            }

            row -= Leading;
        }

        // At the foot: the mark again and the page count.
        var foot = $"{mark}  ·  {pageLabel.Replace("{page}", number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{pages}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)}";

        content.Write(Ascii($"BT 0.72 0.11 0.11 rg /F1 7 Tf {Number(Margin)} 28 Td "));
        content.Write(Literal(foot));
        content.Write(Ascii(" Tj ET"));

        return content.ToArray();
    }

    /// <summary>
    /// A text as a literal string of PDF: between parentheses, with its parentheses and backslashes escaped.
    /// </summary>
    private static byte[] Literal(string text)
    {
        var bytes = new List<byte> { (byte)'(' };

        foreach (var value in Encode(Clean(text)))
        {
            if (value is (byte)'(' or (byte)')' or (byte)'\\')
            {
                bytes.Add((byte)'\\');
            }

            bytes.Add(value);
        }

        bytes.Add((byte)')');

        return [.. bytes];
    }

    /// <summary>
    /// A text in one line: the line breaks, the tabs and the other control characters become spaces.
    /// </summary>
    private static string Clean(string text) => string.Create(text.Length, text, (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = char.IsControl(source[i]) ? ' ' : source[i];
        }
    });

    /// <summary>
    /// The byte of a character in WinAnsi: ASCII and Latin-1 keep their code, and the punctuation Windows-1252 puts
    /// between 128 and 159 takes its own.
    /// </summary>
    private static byte Encode(char value) => value switch
    {
        < (char)0x80 => (byte)value,
        >= (char)0xA0 and <= (char)0xFF => (byte)value,
        '€' => 0x80,
        '‚' => 0x82,
        '„' => 0x84,
        '…' => 0x85,
        '‘' => 0x91,
        '’' => 0x92,
        '“' => 0x93,
        '”' => 0x94,
        '•' => 0x95,
        '–' => 0x96,
        '—' => 0x97,
        '™' => 0x99,
        // A narrow or no-break space of any kind is a space.
        ' ' or ' ' or ' ' => 0x20,
        _ => (byte)'?'
    };

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record Row(string Text, bool Bold, bool IsRule);
}
