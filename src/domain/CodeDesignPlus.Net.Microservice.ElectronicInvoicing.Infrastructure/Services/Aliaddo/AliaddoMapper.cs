using System.Globalization;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo.Models;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// The paths of the API of Nitro by type of document (<c>docs/aliaddo/api/</c>).
/// </summary>
public static class AliaddoPaths
{
    /// <summary>
    /// Where a document is created.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <returns>The path.</returns>
    public static string CreateOf(ElectronicDocumentType type) => type switch
    {
        ElectronicDocumentType.Invoice => "/v2/documents/invoices",
        ElectronicDocumentType.CreditNote => "/v2/documents/invoices/credit-notes",
        ElectronicDocumentType.DebitNote => "/v2/documents/invoices/debit-notes",
        ElectronicDocumentType.PosTicket => "/v2/documents/equivalent-documents",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Where a document is read by its id in Nitro.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <param name="id">The id in Nitro.</param>
    /// <returns>The path.</returns>
    /// <remarks>
    /// The POS ticket is read at <c>/v2/documents/equivalent-document/{id}</c>, in singular, as the documentation writes it
    /// (<c>documentos-equivalentes-consultar-por-id.md</c>); TODO(pendings/663): confirm it in mode = Test.
    /// </remarks>
    public static string ByIdOf(ElectronicDocumentType type, string id) => type switch
    {
        ElectronicDocumentType.Invoice => $"/v2/documents/invoices/{Uri.EscapeDataString(id)}",
        ElectronicDocumentType.CreditNote => $"/v2/documents/invoices/credit-notes/{Uri.EscapeDataString(id)}",
        ElectronicDocumentType.DebitNote => $"/v2/documents/invoices/debit-notes/{Uri.EscapeDataString(id)}",
        ElectronicDocumentType.PosTicket => $"/v2/documents/equivalent-document/{Uri.EscapeDataString(id)}",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Where a document is found by its prefix and consecutive.
    /// </summary>
    /// <param name="type">The type of document.</param>
    /// <param name="number">The number.</param>
    /// <returns>The path with its query.</returns>
    public static string ByNumberOf(ElectronicDocumentType type, DocumentNumber number)
    {
        var path = type switch
        {
            ElectronicDocumentType.Invoice => "/v2/documents/invoices/find-by-prefix-and-consecutive",
            ElectronicDocumentType.CreditNote => "/v2/documents/invoices/credit-notes/find-by-prefix-and-consecutive",
            ElectronicDocumentType.DebitNote => "/v2/documents/invoices/debit-notes/find-by-prefix-and-consecutive",
            ElectronicDocumentType.PosTicket => "/v2/documents/equivalent-documents/find-by-prefix-and-consecutive",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        return $"{path}?prefix={Uri.EscapeDataString(number.Prefix)}&consecutive={number.Consecutive.ToString(CultureInfo.InvariantCulture)}";
    }
}

/// <summary>
/// Turns a neutral document into the body Nitro expects.
/// </summary>
public static class AliaddoMapper
{
    /// <summary>
    /// What Nitro would refuse of a document, said before sending it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The reasons; empty when it can be sent.</returns>
    public static List<string> Problems(ProviderDocument document)
    {
        var problems = new List<string>();

        if (document.Number is null || document.NumberingRange is null || string.IsNullOrWhiteSpace(document.NumberingRange.TechnicalKey))
        {
            problems.Add(Errors.AliaddoNeedsNumberingResolution.GetMessage());
        }
        else if (!long.TryParse(document.NumberingRange.ResolutionNumber, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            problems.Add(Errors.AliaddoNeedsNumericResolution.GetMessage());
        }

        // The documentation types the quantity as an integer of at least one.
        if (document.Content.Lines.Any(x => x.Quantity != decimal.Truncate(x.Quantity) || x.Quantity < 1))
        {
            problems.Add(Errors.AliaddoNeedsWholeQuantities.GetMessage());
        }

        return problems;
    }

    /// <summary>
    /// The body of the send.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="document">The document; <see cref="Problems"/> found nothing in it.</param>
    /// <param name="options">The options of Aliaddo.</param>
    /// <returns>The body.</returns>
    public static AliaddoDocumentRequest ToRequest(ProviderIssuer issuer, ProviderDocument document, AliaddoOptions options)
    {
        var content = document.Content;
        var digits = content.Totals.CurrencyDecimalDigits;
        var kind = options.For(document.Type);
        var range = document.NumberingRange!;

        var request = new AliaddoDocumentRequest
        {
            Code = kind.Code,
            TypeOperation = kind.OperationType,
            Mode = issuer.Environment.ToString(),
            Format = kind.Format,
            TestSetId = issuer.Environment == IssuerEnvironment.Habilitation ? issuer.TestSetId : null,
            Prefix = document.Number!.Prefix,
            Consecutive = document.Number.Consecutive,
            CurrencyCode = content.Totals.Currency,
            DueAt = DateOf(content.DueDate),
            PaymentMeanCode = content.PaymentMeans.MethodCode,
            Resolution = new AliaddoResolution
            {
                Key = range.TechnicalKey!,
                Prefix = range.Prefix,
                Number = long.Parse(range.ResolutionNumber, NumberStyles.None, CultureInfo.InvariantCulture),
                RangeInitial = range.From,
                RangeFinal = range.To,
                ValidFrom = DateOf(range.ValidFrom),
                ValidUntil = DateOf(range.ValidUntil)
            },
            Customer = CustomerOf(content.Buyer),
            Lines = [.. content.Lines.Select(x => LineOf(x, digits, options))],
            Totals = new AliaddoTotals
            {
                Amount = Whole(content.Totals.GrossAmount, digits),
                PrepaymentAmount = Whole(content.Totals.PrepaymentAmount, digits),
                TaxesAmount = Whole(content.Totals.TaxAmount, digits),
                WithholdingAmount = Whole(content.Totals.WithholdingAmount, digits),
                DiscountsAmount = 0,
                ChargesAmount = 0
            },
            Notes = content.Notes.Count > 0 ? [.. content.Notes] : null
        };

        if (content.BillingReference is { } reference)
        {
            request.ConceptCode = content.CorrectionConceptCode;
            request.InvoicePrefix = reference.Prefix;
            request.InvoiceConsecutive = reference.Consecutive;
            request.InvoiceCuv = reference.Cufe;
            request.InvoiceIssuedAt = DateOf(reference.IssueDate);
            request.InvoiceSequence = new DocumentNumber(reference.Prefix, reference.Consecutive).ToString();
        }

        return request;
    }

    /// <summary>
    /// Minor units in whole units of the currency, rounded half away from zero: the fields Nitro types as integers.
    /// </summary>
    /// <param name="minor">The amount in minor units.</param>
    /// <param name="digits">The decimal digits of the currency.</param>
    /// <returns>The amount in whole units.</returns>
    public static long Whole(long minor, short digits) => (long)Math.Round(Major(minor, digits), 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Minor units in units of the currency, with its decimals: the fields Nitro types as numbers.
    /// </summary>
    /// <param name="minor">The amount in minor units.</param>
    /// <param name="digits">The decimal digits of the currency.</param>
    /// <returns>The amount.</returns>
    public static decimal Major(long minor, short digits) => minor / Pow10(digits);

    /// <summary>
    /// Basis points as the percentage Nitro expects (1900 is 19).
    /// </summary>
    /// <param name="basisPoints">The rate in basis points.</param>
    /// <returns>The percentage.</returns>
    public static decimal Percentage(int basisPoints) => basisPoints / 100m;

    private static AliaddoCustomer CustomerOf(Buyer buyer) => new()
    {
        CompanyName = buyer.Name,
        PersonType = buyer.PersonTypeCode,
        // TODO(pendings/663): Nitro asks for first and last names also for a company; the name goes in both until a test
        // in mode = Test says what it expects.
        FirstName = string.IsNullOrWhiteSpace(buyer.FirstName) ? buyer.Name : buyer.FirstName,
        LastName = string.IsNullOrWhiteSpace(buyer.LastName) ? buyer.Name : buyer.LastName,
        Identification = buyer.IdentificationNumber,
        DigitCheck = buyer.CheckDigit ?? string.Empty,
        IdentificationTypeCode = buyer.IdentificationTypeCode,
        Email = buyer.Email,
        Phone = buyer.Phone,
        ResponsibleFor = buyer.TaxLiabilityCode,
        // TODO(pendings/663): the field is one text; several responsibilities go separated by ";", as in the XML of the
        // tax authority, until a test confirms it.
        Responsibilities = string.Join(';', buyer.TaxResponsibilities ?? []),
        BillingAddress = buyer.Address is null ? null : AddressOf(buyer.Address)
    };

    private static AliaddoAddress AddressOf(PostalAddress address) => new()
    {
        Address = address.Street,
        CountryCode = address.CountryCode,
        CountryName = address.CountryName,
        RegionCode = address.RegionCode,
        RegionName = address.RegionName,
        CityCode = address.CityCode,
        CityName = address.CityName,
        PostalCode = address.PostalCode
    };

    private static AliaddoLine LineOf(DocumentLine line, short digits, AliaddoOptions options)
    {
        var quantity = (long)line.Quantity;
        var discount = Major(line.DiscountAmount, digits);

        return new AliaddoLine
        {
            Code = line.Code,
            Name = line.Description,
            Description = line.Description,
            UnitMeasurementCode = line.UnitCode,
            Price = Whole(line.UnitPrice, digits),
            Quantity = quantity,
            DiscountAmount = Whole(line.DiscountAmount, digits),
            // The discount of Nitro is per unit and its total times the quantity is the discount of the line.
            Discounts = line.DiscountAmount > 0 ? [new AliaddoAdjustment { Name = options.LineDiscountName, Type = "F", Rate = Math.Round(discount / quantity, digits), Amount = Math.Round(discount / quantity, digits) }] : null,
            Taxes = [.. (line.Taxes ?? []).Select(x => TaxOf(x, digits))],
            Withholdings = [.. (line.Withholdings ?? []).Select(x => TaxOf(x, digits))]
        };
    }

    private static AliaddoTax TaxOf(LineTax tax, short digits) => new()
    {
        Name = tax.Name,
        Code = tax.Code,
        Type = "P",
        Rate = Percentage(tax.RateBasisPoints),
        Amount = Major(tax.Amount, digits),
        Base = Major(tax.BaseAmount, digits)
    };

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
