using System.Text.Json.Serialization;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo.Models;

/// <summary>
/// The body of <c>POST /v2/documents/invoices</c>, its notes (<c>…/credit-notes</c>, <c>…/debit-notes</c>) and the POS
/// ticket (<c>/v2/documents/equivalent-documents</c>): the same fields, and the notes add the reference to the invoice.
/// </summary>
/// <remarks>
/// Only the fields this service fills; the names are those of <c>docs/aliaddo/api/*-crear-*.md</c>. The amounts that the
/// documentation types as <c>integer</c> go in whole units of the currency; those typed as <c>number</c>, with the
/// decimals of the currency.
/// </remarks>
public sealed class AliaddoDocumentRequest
{
    [JsonPropertyName("code")] public string Code { get; set; } = null!;
    [JsonPropertyName("typeOperation")] public string TypeOperation { get; set; } = null!;
    [JsonPropertyName("mode")] public string Mode { get; set; } = null!;
    [JsonPropertyName("format")] public string Format { get; set; } = null!;
    [JsonPropertyName("testSetId")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TestSetId { get; set; }
    [JsonPropertyName("prefix")] public string Prefix { get; set; } = null!;
    [JsonPropertyName("consecutive")] public long Consecutive { get; set; }
    [JsonPropertyName("currencyCode")] public string CurrencyCode { get; set; } = null!;
    [JsonPropertyName("dueAt")] public string DueAt { get; set; } = null!;
    [JsonPropertyName("paymentMeanCode")] public string PaymentMeanCode { get; set; } = null!;
    [JsonPropertyName("resolution")] public AliaddoResolution Resolution { get; set; } = null!;
    [JsonPropertyName("customer")] public AliaddoCustomer Customer { get; set; } = null!;
    [JsonPropertyName("lines")] public List<AliaddoLine> Lines { get; set; } = [];
    [JsonPropertyName("totals")] public AliaddoTotals Totals { get; set; } = null!;
    [JsonPropertyName("notes")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Notes { get; set; }
    [JsonPropertyName("conceptCode")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ConceptCode { get; set; }
    [JsonPropertyName("invoicePrefix")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoicePrefix { get; set; }
    [JsonPropertyName("invoiceConsecutive")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? InvoiceConsecutive { get; set; }
    [JsonPropertyName("invoiceCuv")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoiceCuv { get; set; }
    [JsonPropertyName("invoiceIssuedAt")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoiceIssuedAt { get; set; }
    [JsonPropertyName("invoiceSequence")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoiceSequence { get; set; }
}

/// <summary>
/// The numbering resolution sent with each document (<c>Resolucion</c>).
/// </summary>
public sealed class AliaddoResolution
{
    [JsonPropertyName("key")] public string Key { get; set; } = null!;
    [JsonPropertyName("prefix")] public string Prefix { get; set; } = null!;
    [JsonPropertyName("number")] public long Number { get; set; }
    [JsonPropertyName("rangeInitial")] public long RangeInitial { get; set; }
    [JsonPropertyName("rangeFinal")] public long RangeFinal { get; set; }
    [JsonPropertyName("validFrom")] public string ValidFrom { get; set; } = null!;
    [JsonPropertyName("validUntil")] public string ValidUntil { get; set; } = null!;
}

/// <summary>
/// The buyer (<c>Cliente</c>).
/// </summary>
public sealed class AliaddoCustomer
{
    [JsonPropertyName("companyName")] public string CompanyName { get; set; } = null!;
    [JsonPropertyName("personType")] public string PersonType { get; set; } = null!;
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = null!;
    [JsonPropertyName("lastName")] public string LastName { get; set; } = null!;
    [JsonPropertyName("identification")] public string Identification { get; set; } = null!;
    [JsonPropertyName("digitCheck")] public string DigitCheck { get; set; } = null!;
    [JsonPropertyName("identificationTypeCode")] public string IdentificationTypeCode { get; set; } = null!;
    [JsonPropertyName("email")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; set; }
    [JsonPropertyName("phone")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; set; }
    [JsonPropertyName("responsibleFor")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ResponsibleFor { get; set; }
    [JsonPropertyName("responsibilities")] public string Responsibilities { get; set; } = null!;
    [JsonPropertyName("BillingAddress")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AliaddoAddress? BillingAddress { get; set; }
}

/// <summary>
/// An address (<c>Direccion</c>).
/// </summary>
public sealed class AliaddoAddress
{
    [JsonPropertyName("address")] public string Address { get; set; } = null!;
    [JsonPropertyName("countryCode")] public string CountryCode { get; set; } = null!;
    [JsonPropertyName("countryName")] public string CountryName { get; set; } = null!;
    [JsonPropertyName("regionCode")] public string RegionCode { get; set; } = null!;
    [JsonPropertyName("regionName")] public string RegionName { get; set; } = null!;
    [JsonPropertyName("cityCode")] public string CityCode { get; set; } = null!;
    [JsonPropertyName("cityName")] public string CityName { get; set; } = null!;
    [JsonPropertyName("postalCode")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PostalCode { get; set; }
}

/// <summary>
/// A line (<c>LineaProducto</c>).
/// </summary>
public sealed class AliaddoLine
{
    [JsonPropertyName("code")] public string Code { get; set; } = null!;
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("description")] public string Description { get; set; } = null!;
    [JsonPropertyName("unitMeasurementCode")] public string UnitMeasurementCode { get; set; } = null!;
    [JsonPropertyName("price")] public long Price { get; set; }
    [JsonPropertyName("quantity")] public long Quantity { get; set; }
    [JsonPropertyName("discountAmount")] public long DiscountAmount { get; set; }
    [JsonPropertyName("discounts")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<AliaddoAdjustment>? Discounts { get; set; }
    [JsonPropertyName("taxes")] public List<AliaddoTax> Taxes { get; set; } = [];
    [JsonPropertyName("withholdings")] public List<AliaddoTax> Withholdings { get; set; } = [];
}

/// <summary>
/// A discount of a line (<c>Descuento</c>), per unit.
/// </summary>
public sealed class AliaddoAdjustment
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("type")] public string Type { get; set; } = null!;
    [JsonPropertyName("rate")] public decimal Rate { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
}

/// <summary>
/// A tax or a withholding of a line (<c>Impuestos</c>, <c>Retencioness</c>), as a percentage.
/// </summary>
public sealed class AliaddoTax
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("code")] public string Code { get; set; } = null!;
    [JsonPropertyName("type")] public string Type { get; set; } = null!;
    [JsonPropertyName("rate")] public decimal Rate { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("base")] public decimal Base { get; set; }
}

/// <summary>
/// The totals (<c>Totales</c>), in whole units of the currency.
/// </summary>
public sealed class AliaddoTotals
{
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("prepaymentAmount")] public long PrepaymentAmount { get; set; }
    [JsonPropertyName("taxesAmount")] public long TaxesAmount { get; set; }
    [JsonPropertyName("withholdingAmount")] public long WithholdingAmount { get; set; }
    [JsonPropertyName("discountsAmount")] public long DiscountsAmount { get; set; }
    [JsonPropertyName("chargesAmount")] public long ChargesAmount { get; set; }
}

/// <summary>
/// The answer to a send (<c>Response</c>): <c>Id</c>, <c>Cufe</c>, <c>Sequence</c>, <c>Status</c> and <c>StatusReasons</c>.
/// </summary>
public sealed class AliaddoSubmissionResponse
{
    public string? Id { get; set; }
    public string? Cufe { get; set; }
    public string? Sequence { get; set; }
    public string? Status { get; set; }
    public List<string>? StatusReasons { get; set; }
}

/// <summary>
/// A document as <c>GET /v2/documents/{type}/{id}</c> and <c>…/find-by-prefix-and-consecutive</c> return it; only the
/// fields this service reads.
/// </summary>
public sealed class AliaddoDocumentResponse
{
    public string? Id { get; set; }
    public string? Prefix { get; set; }
    public long? Consecutive { get; set; }
    public string? Cuv { get; set; }
    public string? IssuedAt { get; set; }
    public string? Status { get; set; }
    public string? DianDeliveryStatus { get; set; }
    public List<string>? StatusReasons { get; set; }
    public string? RejectReason { get; set; }
    public string? UrlPdf { get; set; }
    public string? UrlXmlDocument { get; set; }
    public string? UrlXmlAttachedDocument { get; set; }
}

/// <summary>
/// A numbering resolution as <c>GET /v2/resolutions/resolutions-dian</c> returns it.
/// </summary>
public sealed class AliaddoResolutionResponse
{
    public string? Key { get; set; }
    public string? Prefix { get; set; }
    public long Number { get; set; }
    public long RangeInitial { get; set; }
    public long RangeFinal { get; set; }
    public string? ValidFrom { get; set; }
    public string? ValidUntil { get; set; }
}

/// <summary>
/// The body of <c>POST /companies/isv</c>: the child organization and its user.
/// </summary>
public sealed class AliaddoChildRequest
{
    [JsonPropertyName("company")] public AliaddoChildCompany Company { get; set; } = null!;
    [JsonPropertyName("user")] public AliaddoChildUser User { get; set; } = null!;
}

/// <summary>
/// The child organization (<c>AddCompany</c>).
/// </summary>
public sealed class AliaddoChildCompany
{
    [JsonPropertyName("identificationTypeId")] public string IdentificationTypeId { get; set; } = null!;
    [JsonPropertyName("identificationCheck")] public string IdentificationCheck { get; set; } = null!;
    [JsonPropertyName("identificationNumber")] public string IdentificationNumber { get; set; } = null!;
    [JsonPropertyName("email")] public string Email { get; set; } = null!;
    [JsonPropertyName("phone")] public string Phone { get; set; } = null!;
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
}

/// <summary>
/// The user of the child organization (<c>AddUser</c>).
/// </summary>
public sealed class AliaddoChildUser
{
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = null!;
    [JsonPropertyName("lastName")] public string LastName { get; set; } = null!;
    [JsonPropertyName("email")] public string Email { get; set; } = null!;
    [JsonPropertyName("phoneNumber")] public string PhoneNumber { get; set; } = null!;
}

/// <summary>
/// The answer to <c>POST /companies/isv</c>; only what this service reads.
/// </summary>
public sealed class AliaddoChildResponse
{
    public string? Id { get; set; }
    public string? ContractSignatureStatus { get; set; }
    public bool? Enabled { get; set; }
}
