using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// What the simulated provider writes for a person to read (the graphic representation and the email to the buyer), in
/// one language.
/// </summary>
/// <remarks>
/// Each language is an embedded resource <c>Resources/simulated.&lt;language&gt;.json</c> with the same keys; the
/// language of a deployment is <c>Simulated:Language</c>. The separators of the numbers are part of the language, so the
/// amounts read the same on every machine, whatever cultures its operating system has.
/// </remarks>
public sealed class SimulatedTexts
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static readonly ConcurrentDictionary<string, SimulatedTexts?> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The mark every page carries: the document is a test and has no fiscal validity.
    /// </summary>
    public string Mark { get; set; } = string.Empty;

    /// <summary>
    /// The short mark written across every page.
    /// </summary>
    public string Watermark { get; set; } = string.Empty;

    /// <summary>
    /// What separates the thousands of an amount.
    /// </summary>
    public string GroupSeparator { get; set; } = string.Empty;

    /// <summary>
    /// What separates the decimals of an amount.
    /// </summary>
    public string DecimalSeparator { get; set; } = string.Empty;

    /// <summary>
    /// The name of each type of document, by the name of its <see cref="ElectronicDocumentType"/>.
    /// </summary>
    public Dictionary<string, string> DocumentTypes { get; set; } = [];

    /// <summary>
    /// Label of a tax id.
    /// </summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>
    /// Label of an address.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Label of an email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Label of a phone.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Label of the issue date.
    /// </summary>
    public string IssueDate { get; set; } = string.Empty;

    /// <summary>
    /// Label of the due date.
    /// </summary>
    public string DueDate { get; set; } = string.Empty;

    /// <summary>
    /// The sentence of the numbering resolution, with <c>{number}</c>, <c>{prefix}</c>, <c>{from}</c>, <c>{to}</c>,
    /// <c>{validFrom}</c> and <c>{validUntil}</c>.
    /// </summary>
    public string Resolution { get; set; } = string.Empty;

    /// <summary>
    /// Label of the form of payment.
    /// </summary>
    public string PaymentForm { get; set; } = string.Empty;

    /// <summary>
    /// Label of the method of payment.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Title of the data of the buyer.
    /// </summary>
    public string Buyer { get; set; } = string.Empty;

    /// <summary>
    /// Label of a name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Label of the identification of the buyer.
    /// </summary>
    public string Identification { get; set; } = string.Empty;

    /// <summary>
    /// Label of the document a note corrects.
    /// </summary>
    public string Corrects { get; set; } = string.Empty;

    /// <summary>
    /// Label of the concept of a correction.
    /// </summary>
    public string CorrectionConcept { get; set; } = string.Empty;

    /// <summary>
    /// Label of the cash register of a point of sale document.
    /// </summary>
    public string Register { get; set; } = string.Empty;

    /// <summary>
    /// Label of the type of the cash register.
    /// </summary>
    public string RegisterType { get; set; } = string.Empty;

    /// <summary>
    /// Label of the place of the cash register.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Label of the cashier.
    /// </summary>
    public string Cashier { get; set; } = string.Empty;

    /// <summary>
    /// Label of the code of the sale.
    /// </summary>
    public string SaleCode { get; set; } = string.Empty;

    /// <summary>
    /// Header of the code of a line.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Header of the description of a line.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Header of the quantity of a line.
    /// </summary>
    public string Quantity { get; set; } = string.Empty;

    /// <summary>
    /// Header of the unit price of a line.
    /// </summary>
    public string UnitPrice { get; set; } = string.Empty;

    /// <summary>
    /// Header of the discount of a line.
    /// </summary>
    public string Discount { get; set; } = string.Empty;

    /// <summary>
    /// Header of the amount of a line.
    /// </summary>
    public string Total { get; set; } = string.Empty;

    /// <summary>
    /// Title of the taxes.
    /// </summary>
    public string Taxes { get; set; } = string.Empty;

    /// <summary>
    /// Title of the withholdings.
    /// </summary>
    public string Withholdings { get; set; } = string.Empty;

    /// <summary>
    /// Label of the base of a tax.
    /// </summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>
    /// Label of the amount of a tax.
    /// </summary>
    public string Amount { get; set; } = string.Empty;

    /// <summary>
    /// Label of the gross amount.
    /// </summary>
    public string GrossAmount { get; set; } = string.Empty;

    /// <summary>
    /// Label of the discounts.
    /// </summary>
    public string Discounts { get; set; } = string.Empty;

    /// <summary>
    /// Label of the subtotal.
    /// </summary>
    public string Subtotal { get; set; } = string.Empty;

    /// <summary>
    /// Label of the total of the taxes.
    /// </summary>
    public string TaxTotal { get; set; } = string.Empty;

    /// <summary>
    /// Label of the total of the withholdings.
    /// </summary>
    public string WithholdingTotal { get; set; } = string.Empty;

    /// <summary>
    /// Label of the prepayments.
    /// </summary>
    public string Prepayments { get; set; } = string.Empty;

    /// <summary>
    /// Label of the payable amount.
    /// </summary>
    public string Payable { get; set; } = string.Empty;

    /// <summary>
    /// Title of the notes.
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Label of the simulated CUFE.
    /// </summary>
    public string Cufe { get; set; } = string.Empty;

    /// <summary>
    /// The sentence that says where the document comes from.
    /// </summary>
    public string Disclaimer { get; set; } = string.Empty;

    /// <summary>
    /// The page count, with <c>{page}</c> and <c>{pages}</c>.
    /// </summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>
    /// The subject of the email to the buyer, with <c>{type}</c> and <c>{number}</c>.
    /// </summary>
    public string EmailSubject { get; set; } = string.Empty;

    /// <summary>
    /// The title of the email to the buyer, with <c>{number}</c>.
    /// </summary>
    public string EmailTitle { get; set; } = string.Empty;

    /// <summary>
    /// The greeting of the email, with <c>{buyer}</c>.
    /// </summary>
    public string EmailGreeting { get; set; } = string.Empty;

    /// <summary>
    /// What the email delivers, with <c>{issuer}</c>, <c>{type}</c>, <c>{number}</c> and <c>{total}</c>.
    /// </summary>
    public string EmailIntro { get; set; } = string.Empty;

    /// <summary>
    /// The warning of the email: the document is a test.
    /// </summary>
    public string EmailWarning { get; set; } = string.Empty;

    /// <summary>
    /// The sentence of the email about its attachments.
    /// </summary>
    public string EmailAttachments { get; set; } = string.Empty;

    /// <summary>
    /// The texts of a language.
    /// </summary>
    /// <param name="language">The language (<c>es</c>) or a culture of it (<c>es-CO</c>).</param>
    /// <returns>The texts, or null when the language has no resource.</returns>
    public static SimulatedTexts? Find(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return Loaded.GetOrAdd(language.Trim().Split('-', '_')[0], Load);
    }

    /// <summary>
    /// The name of a type of document.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Its name in the language, or the name of the type when the language does not say it.</returns>
    public string NameOf(ElectronicDocumentType type) => DocumentTypes.TryGetValue(type.ToString(), out var name) ? name : type.ToString();

    /// <summary>
    /// How the numbers are written in the language.
    /// </summary>
    /// <returns>The format, with the separators of the language.</returns>
    public NumberFormatInfo NumberFormat() => new() { NumberGroupSeparator = GroupSeparator, NumberDecimalSeparator = DecimalSeparator };

    private static SimulatedTexts? Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = $"simulated.{language.ToLowerInvariant()}.json";

        using var stream = assembly.GetManifestResourceStream(name);

        return stream is null ? null : System.Text.Json.JsonSerializer.Deserialize<SimulatedTexts>(stream, Json);
    }
}
