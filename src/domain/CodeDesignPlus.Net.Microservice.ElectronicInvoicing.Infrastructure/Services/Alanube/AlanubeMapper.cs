using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

/// <summary>
/// The paths of the API of Alanube by type of document (<c>docs/alanube/api/</c>).
/// </summary>
public static class AlanubePaths
{
    /// <summary>
    /// Where the companies are listed and created.
    /// </summary>
    public const string Companies = "companies";

    /// <summary>
    /// The company of the token.
    /// </summary>
    public const string OwnCompany = "company";

    /// <summary>
    /// Where the test sets of the tax authority are sent.
    /// </summary>
    public const string TestSets = "test-sets";

    /// <summary>
    /// Where a document is created.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <returns>The path.</returns>
    public static string CreateOf(ElectronicDocumentType type) => type switch
    {
        ElectronicDocumentType.Invoice => "invoices",
        ElectronicDocumentType.CreditNote => "credit-notes",
        ElectronicDocumentType.DebitNote => "debit-notes",
        ElectronicDocumentType.PosTicket => "equivalent-documents/pos",
        ElectronicDocumentType.PosAdjustmentNote => "adjustment-note-equivalent-documents",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Where a document is read by its id in Alanube.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <param name="id">The id in Alanube.</param>
    /// <returns>The path.</returns>
    public static string ByIdOf(ElectronicDocumentType type, string id)
    {
        var collection = type switch
        {
            ElectronicDocumentType.Invoice => "invoices",
            ElectronicDocumentType.CreditNote => "credit-notes",
            ElectronicDocumentType.DebitNote => "debit-notes",
            ElectronicDocumentType.PosTicket => "equivalent-documents",
            ElectronicDocumentType.PosAdjustmentNote => "adjustment-note-equivalent-documents",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        return $"{collection}/{Uri.EscapeDataString(id)}";
    }

    /// <summary>
    /// Where the documents of a company are found by their prefix and number.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <param name="companyId">The id of the company in Alanube.</param>
    /// <param name="number">The number.</param>
    /// <param name="equivalentDocumentType">The code of the point of sale ticket in the list of equivalent documents, which that list needs with the number.</param>
    /// <returns>The path with its query, or null when Alanube does not list that type.</returns>
    public static string? ByNumberOf(ElectronicDocumentType type, string companyId, DocumentNumber number, string? equivalentDocumentType)
    {
        var query = $"idCompany={Uri.EscapeDataString(companyId)}&prefix={Uri.EscapeDataString(number.Prefix)}&number={number.Consecutive.ToString(CultureInfo.InvariantCulture)}";

        return type switch
        {
            ElectronicDocumentType.Invoice => $"invoices?{query}",
            ElectronicDocumentType.CreditNote => $"credit-notes?{query}",
            ElectronicDocumentType.DebitNote => $"debit-notes?{query}",
            ElectronicDocumentType.PosTicket when !string.IsNullOrWhiteSpace(equivalentDocumentType) => $"equivalent-documents?type={Uri.EscapeDataString(equivalentDocumentType)}&{query}",
            _ => null
        };
    }
}

/// <summary>
/// Turns a neutral document into the body Alanube expects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Amounts.</b> Alanube takes every amount as a number with the decimals of the currency: the minor units are divided
/// by ten to the decimal digits of the currency. The rates go as text with two decimals (1900 basis points are
/// <c>"19.00"</c>).
/// </para>
/// <para>
/// <b>Totals.</b> The discount of a line lowers its subtotal (<c>discountAmount</c> of the item), so the gross total is
/// the sum of the subtotals and the discount of the document is zero. The payable total is the gross plus the taxes,
/// without subtracting the prepayments: the tax authority rejects it otherwise (rule FAU14, seen in the sandbox on
/// 2026-11-09), and the prepayments go apart in <c>advanceTotal</c>.
/// </para>
/// <para>
/// <b>The company.</b> Every document carries the whole data of the issuer and not only its id: without it Alanube
/// answers <c>AEP6012</c> (seen in the sandbox on 2026-11-09).
/// </para>
/// </remarks>
public static class AlanubeMapper
{
    /// <summary>
    /// What Alanube would refuse of a document, said before sending it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The reasons; empty when it can be sent.</returns>
    public static List<string> Problems(ProviderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<string>();

        if (document.Number is null)
        {
            problems.Add(Errors.DocumentNeedsNumber.GetMessage());
        }

        var needsResolution = document.Type is ElectronicDocumentType.Invoice or ElectronicDocumentType.PosTicket;
        var needsTechnicalKey = document.Type == ElectronicDocumentType.Invoice;

        if (needsResolution && (document.NumberingRange is null || (needsTechnicalKey && string.IsNullOrWhiteSpace(document.NumberingRange.TechnicalKey))))
        {
            problems.Add(Errors.AliaddoNeedsNumberingResolution.GetMessage());
        }

        if (document.Type == ElectronicDocumentType.PosAdjustmentNote && !int.TryParse(document.Content.CorrectionConceptCode, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            problems.Add(Errors.InvalidContent.GetMessage());
        }

        return problems;
    }

    /// <summary>
    /// The body of the send.
    /// </summary>
    /// <param name="issuer">The issuer; it already exists in Alanube.</param>
    /// <param name="document">The document; <see cref="Problems"/> found nothing in it.</param>
    /// <param name="options">The options of Alanube.</param>
    /// <returns>The body.</returns>
    public static JsonObject ToRequest(ProviderIssuer issuer, ProviderDocument document, AlanubeOptions options)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var content = document.Content;
        var digits = content.Totals.CurrencyDecimalDigits;
        var pointOfSale = IsPointOfSale(document.Type);
        var kind = options.For(document.Type);
        var number = document.Number!;

        var body = new JsonObject();

        if (!string.IsNullOrWhiteSpace(kind.DocumentType) && document.Type is ElectronicDocumentType.Invoice or ElectronicDocumentType.CreditNote)
        {
            body["documentType"] = kind.DocumentType;
        }

        if (document.Type is ElectronicDocumentType.CreditNote or ElectronicDocumentType.DebitNote or ElectronicDocumentType.PosAdjustmentNote)
        {
            body["prefix"] = number.Prefix;
        }

        // The number is a number in the invoice and the notes, and a text in the documents of the point of sale.
        body["number"] = pointOfSale ? JsonValue.Create(number.Consecutive.ToString(CultureInfo.InvariantCulture)) : JsonValue.Create(number.Consecutive);

        if (document.Type is ElectronicDocumentType.Invoice or ElectronicDocumentType.PosTicket)
        {
            body["resolution"] = ResolutionOf(document.NumberingRange!, document.Type == ElectronicDocumentType.Invoice);
        }

        AddCorrection(body, document.Type, content, kind);

        body["company"] = CompanyOf(issuer, options);
        body["customer"] = CustomerOf(content.Buyer, pointOfSale);
        body["items"] = new JsonArray([.. content.Lines.Select(x => (JsonNode)ItemOf(x, digits, pointOfSale))]);
        body["totalAmounts"] = TotalsOf(content, digits, pointOfSale);
        body["payments"] = new JsonArray(PaymentOf(content, options));

        if (content.Prepayments is { Count: > 0 } prepayments && document.Type is ElectronicDocumentType.Invoice or ElectronicDocumentType.DebitNote)
        {
            body["advancePayments"] = new JsonArray([.. prepayments.Select(x => (JsonNode)new JsonObject
            {
                ["advanceId"] = x.Id,
                ["advanceAmount"] = Major(x.Amount, digits),
                ["advanceReceivedDate"] = DateOf(x.ReceivedOn)
            })]);
        }

        if (content.Notes is { Count: > 0 } notes)
        {
            // The adjustment note of a ticket takes one text; the rest, a list.
            body["note"] = document.Type == ElectronicDocumentType.PosAdjustmentNote ? JsonValue.Create(string.Join('\n', notes)) : new JsonArray([.. notes.Select(x => (JsonNode)JsonValue.Create(x)!)]);
        }

        return body;
    }

    /// <summary>
    /// The company of the issuer as every document carries it.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="options">The options of Alanube.</param>
    /// <returns>The company, with its id in Alanube.</returns>
    public static JsonObject CompanyOf(ProviderIssuer issuer, AlanubeOptions options)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(options);

        var company = FiscalDataOf(issuer, options);

        company["id"] = issuer.ProviderIssuerId;
        company["identificationNumber"] = IdentificationOf(issuer.TaxId);
        company["dv"] = CheckDigitOf(issuer.TaxId);
        company["name"] = issuer.LegalName;

        return company;
    }

    /// <summary>
    /// The body that creates the company of an issuer in Alanube.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="options">The options of Alanube.</param>
    /// <returns>The body.</returns>
    public static JsonObject CompanyRequestOf(ProviderIssuer issuer, AlanubeOptions options)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(options);

        var company = FiscalDataOf(issuer, options);

        company["name"] = issuer.LegalName;
        company["identification"] = IdentificationOf(issuer.TaxId);
        company["dv"] = CheckDigitOf(issuer.TaxId);
        company["useAlegraCertificate"] = options.UseProviderCertificate;
        company["notificationByEmail"] = new JsonObject { ["enabled"] = options.NotifyBuyerByEmail };

        if (options.Webhooks.Enabled && options.Webhooks.Url is { } url)
        {
            company["webhooks"] = WebhooksOf(url, options.Webhooks.Header!, options.Webhooks.Secret!);
        }

        return company;
    }

    /// <summary>
    /// The type of test set of Alanube that enables a type of document.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <returns>The type of test set, or null when no test set enables it.</returns>
    public static string? TestSetTypeOf(ElectronicDocumentType type) => type switch
    {
        ElectronicDocumentType.Invoice or ElectronicDocumentType.CreditNote or ElectronicDocumentType.DebitNote => "invoices",
        ElectronicDocumentType.PosTicket or ElectronicDocumentType.PosAdjustmentNote => "pos",
        _ => null
    };

    /// <summary>
    /// The idempotency key of a send: a UUID version 4, as Alanube demands, derived from the document, its revision and
    /// the body itself.
    /// </summary>
    /// <remarks>
    /// The same send of the same document gets the same key, so a send whose answer was lost and goes again is answered
    /// with the first result instead of issuing twice. A correction (a new revision) is a new key even with the same
    /// body, so a document that failed can be sent again; and a body that changed (the data of the issuer, for example)
    /// is a new key instead of the refusal <c>422</c> of a key reused with another body. Issuing twice is still not
    /// possible: Alanube refuses a number it already received, and that refusal is looked up by its number.
    /// </remarks>
    /// <param name="documentId">The id of the document in this service.</param>
    /// <param name="revision">How many times its content was corrected.</param>
    /// <param name="body">The body exactly as it is sent.</param>
    /// <returns>The key, in its canonical form.</returns>
    public static string IdempotencyKeyOf(Guid documentId, int revision, string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(documentId.ToString("N"), "|", revision.ToString(CultureInfo.InvariantCulture), "|", body)));
        var bytes = hash.AsSpan(0, 16).ToArray();

        // The version (4) and the variant (RFC 4122) bits: the rest are the bits of the hash.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        var hex = Convert.ToHexStringLower(bytes);

        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>
    /// Minor units in units of the currency, with its decimals.
    /// </summary>
    /// <param name="minor">The amount in minor units.</param>
    /// <param name="digits">The decimal digits of the currency.</param>
    /// <returns>The amount.</returns>
    public static decimal Major(long minor, short digits) => decimal.Round(minor / Pow10(digits), digits);

    /// <summary>
    /// Basis points as the rate Alanube expects, a text with two decimals (1900 is <c>19.00</c>).
    /// </summary>
    /// <param name="basisPoints">The rate in basis points.</param>
    /// <returns>The rate.</returns>
    public static string Percentage(int basisPoints) => (basisPoints / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static bool IsPointOfSale(ElectronicDocumentType type) => type is ElectronicDocumentType.PosTicket or ElectronicDocumentType.PosAdjustmentNote;

    private static JsonObject ResolutionOf(ProviderNumberingRange range, bool withTechnicalKey)
    {
        var resolution = new JsonObject
        {
            ["resolutionNumber"] = range.ResolutionNumber,
            ["prefix"] = range.Prefix,
            ["minNumber"] = range.From,
            ["maxNumber"] = range.To,
            ["startDate"] = DateOf(range.ValidFrom),
            ["endDate"] = DateOf(range.ValidUntil)
        };

        if (withTechnicalKey)
        {
            resolution["technicalKey"] = range.TechnicalKey;
        }

        return resolution;
    }

    private static void AddCorrection(JsonObject body, ElectronicDocumentType type, DocumentContent content, AlanubeDocumentOptions kind)
    {
        if (content.BillingReference is not { } reference)
        {
            return;
        }

        if (type is ElectronicDocumentType.CreditNote or ElectronicDocumentType.DebitNote)
        {
            body["conceptCode"] = content.CorrectionConceptCode;
            body["associatedDocuments"] = new JsonArray(new JsonObject
            {
                ["date"] = DateOf(reference.IssueDate),
                ["documentType"] = kind.CorrectedDocumentType,
                ["number"] = reference.Consecutive,
                ["prefix"] = reference.Prefix,
                ["uuid"] = reference.Cufe
            });

            return;
        }

        if (type == ElectronicDocumentType.PosAdjustmentNote)
        {
            body["discrepancy"] = new JsonObject { ["responseCode"] = int.Parse(content.CorrectionConceptCode!, NumberStyles.None, CultureInfo.InvariantCulture) };
            body["documentReference"] = new JsonObject
            {
                ["fullNumber"] = new DocumentNumber(reference.Prefix, reference.Consecutive).ToString(),
                ["cude"] = reference.Cufe,
                ["issueDate"] = DateOf(reference.IssueDate)
            };
        }
    }

    private static JsonObject FiscalDataOf(ProviderIssuer issuer, AlanubeOptions options)
    {
        var data = new JsonObject
        {
            ["organizationType"] = OrganizationTypeOf(issuer.PersonTypeCode),
            ["identificationType"] = issuer.TaxIdTypeCode,
            ["regimeCode"] = string.Join(';', issuer.TaxResponsibilities ?? []),
            ["email"] = issuer.Email,
            ["phone"] = issuer.Phone,
            ["address"] = new JsonObject
            {
                ["address"] = issuer.Address.Street,
                ["city"] = issuer.Address.CityCode,
                ["department"] = issuer.Address.RegionCode,
                ["country"] = issuer.Address.CountryCode,
                ["postalCode"] = issuer.Address.PostalCode
            }
        };

        if (options.TaxSchemeByRegime.TryGetValue(issuer.TaxRegimeCode ?? string.Empty, out var scheme) && !string.IsNullOrWhiteSpace(scheme))
        {
            data["taxCode"] = new JsonObject { ["id"] = scheme };
        }

        return data;
    }

    private static JsonObject CustomerOf(Buyer buyer, bool pointOfSale)
    {
        var customer = new JsonObject
        {
            ["name"] = buyer.Name,
            ["organizationType"] = OrganizationTypeOf(buyer.PersonTypeCode),
            ["identificationType"] = buyer.IdentificationTypeCode,
            ["identificationNumber"] = buyer.IdentificationNumber
        };

        if (!string.IsNullOrWhiteSpace(buyer.CheckDigit))
        {
            customer["dv"] = buyer.CheckDigit;
        }

        if (!string.IsNullOrWhiteSpace(buyer.TaxLiabilityCode))
        {
            customer["taxCode"] = new JsonObject { ["id"] = buyer.TaxLiabilityCode };
        }

        if (!string.IsNullOrWhiteSpace(buyer.Email))
        {
            customer["email"] = buyer.Email;
        }

        if (!string.IsNullOrWhiteSpace(buyer.Phone))
        {
            customer["phone"] = buyer.Phone;
        }

        // The buyer of a ticket has neither responsibilities nor address in the documents of the point of sale.
        if (pointOfSale)
        {
            return customer;
        }

        if (buyer.TaxResponsibilities is { Count: > 0 } responsibilities)
        {
            customer["regimeCode"] = string.Join(';', responsibilities);
        }

        if (buyer.Address is { } address)
        {
            customer["address"] = new JsonObject
            {
                ["address"] = address.Street,
                ["city"] = address.CityCode,
                ["postalCode"] = address.PostalCode,
                ["country"] = address.CountryCode
            };
        }

        return customer;
    }

    private static JsonObject ItemOf(DocumentLine line, short digits, bool pointOfSale)
    {
        var taxes = line.Taxes ?? [];
        var withholdings = line.Withholdings ?? [];
        var taxAmount = taxes.Sum(x => x.Amount);

        var item = new JsonObject();

        if (pointOfSale)
        {
            item["sellersItemIdentification"] = new JsonObject { ["id"] = line.Code };
        }
        else
        {
            item["code"] = line.Code;
        }

        item["description"] = line.Description;
        item["price"] = Major(line.UnitPrice, digits);
        item["quantity"] = line.Quantity;
        item["unitCode"] = line.UnitCode;

        if (line.DiscountAmount > 0 && line.GrossAmount > 0)
        {
            item["discount"] = decimal.Round(line.DiscountAmount * 100m / line.GrossAmount, 2, MidpointRounding.AwayFromZero);
            item["discountAmount"] = Major(line.DiscountAmount, digits);
        }

        item["subtotal"] = Major(line.Amount, digits);
        item["taxAmount"] = Major(taxAmount, digits);
        item["total"] = Major(line.Amount + taxAmount, digits);
        item["taxes"] = new JsonArray([.. taxes.Select(x => (JsonNode)TaxOf(x, digits))]);

        if (withholdings.Count > 0)
        {
            item["withholdings"] = new JsonArray([.. withholdings.Select(x => (JsonNode)TaxOf(x, digits))]);
        }

        return item;
    }

    private static JsonObject TaxOf(LineTax tax, short digits) => new()
    {
        ["taxCode"] = tax.Code,
        ["taxPercentage"] = Percentage(tax.RateBasisPoints),
        ["taxAmount"] = Major(tax.Amount, digits),
        ["taxableAmount"] = Major(tax.BaseAmount, digits)
    };

    private static JsonObject TotalsOf(DocumentContent content, short digits, bool pointOfSale)
    {
        var gross = content.Lines.Sum(x => x.Amount);
        // The base once per line: a line with two taxes on the same base does not count it twice.
        var taxable = content.Lines.Sum(x => (x.Taxes ?? []).Select(t => t.BaseAmount).DefaultIfEmpty(0).Max());
        var taxes = content.Totals.TaxAmount;

        var totals = new JsonObject
        {
            ["grossTotal"] = Major(gross, digits),
            ["taxableTotal"] = Major(taxable, digits),
            ["taxTotal"] = Major(taxes, digits),
            ["discountTotal"] = Major(0, digits),
            ["chargeTotal"] = Major(0, digits)
        };

        if (!pointOfSale)
        {
            totals["advanceTotal"] = Major(content.Totals.PrepaymentAmount, digits);
        }

        totals["payableTotal"] = Major(gross + taxes, digits);

        return totals;
    }

    private static JsonObject PaymentOf(DocumentContent content, AlanubeOptions options)
    {
        var payment = new JsonObject
        {
            ["paymentForm"] = content.PaymentMeans.FormCode,
            ["paymentMethod"] = content.PaymentMeans.MethodCode
        };

        // Alanube asks for the due date only on credit, and refuses it if it is not after the day of issue.
        if (string.Equals(content.PaymentMeans.FormCode, options.CreditPaymentForm, StringComparison.Ordinal))
        {
            payment["paymentDueDate"] = DateOf(content.DueDate);
        }

        return payment;
    }

    private static JsonObject WebhooksOf(Uri url, string header, string secret)
    {
        // One hook per collection of documents that has an emission: each call is only a hint to look at the document.
        var hooks = new JsonObject();

        foreach (var collection in new[] { "invoices", "creditNotes", "debitNotes", "equivalentDocuments" })
        {
            hooks[collection] = new JsonObject
            {
                ["emissionFinished"] = new JsonObject
                {
                    ["url"] = url.ToString(),
                    ["headers"] = new JsonObject { [header] = secret },
                    ["status"] = "active"
                }
            };
        }

        return hooks;
    }

    private static JsonNode? OrganizationTypeOf(string? code) => int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? JsonValue.Create(value) : null;

    private static string IdentificationOf(string taxId) => (taxId ?? string.Empty).Split('-')[0];

    private static string CheckDigitOf(string taxId)
    {
        var parts = (taxId ?? string.Empty).Split('-');

        return parts.Length > 1 ? parts[1] : string.Empty;
    }

    private static decimal Pow10(short digits)
    {
        var factor = 1m;

        for (var i = 0; i < digits; i++)
        {
            factor *= 10;
        }

        return factor;
    }

    private static string DateOf(LocalDate date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
