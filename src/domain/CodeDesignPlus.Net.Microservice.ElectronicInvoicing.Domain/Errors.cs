using CodeDesignPlus.Net.Exceptions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// Error codes of the domain layer; their messages live in <c>Resources/errors.{language}.json</c>.
/// </summary>
public class Errors : IErrorCodes
{
    public static readonly Error UnknownError = new("100");
    public static readonly Error IssuerNotEnabled = new("101");
    public static readonly Error DocumentTypeNotSupported = new("102");
    public static readonly Error NumberingRangeExhausted = new("103");
    public static readonly Error NumberingRangeExpired = new("104");
    public static readonly Error NoteWithoutBillingReference = new("105");
    public static readonly Error TotalsDoNotAddUp = new("106");
    public static readonly Error DocumentAlreadyValidated = new("107");
    public static readonly Error MandateNotAccepted = new("108");
    public static readonly Error QuotaExhausted = new("109");
    public static readonly Error ElectronicDocumentIdIsRequired = new("110");
    public static readonly Error TenantIsRequired = new("111");
    public static readonly Error ReferenceIsRequired = new("112");
    public static readonly Error ReferenceIsTooLong = new("113");
    public static readonly Error CreatedByIsRequired = new("114");
    public static readonly Error BuyerIsRequired = new("115");
    public static readonly Error LinesAreRequired = new("116");
    public static readonly Error CurrencyIsInvalid = new("117");
    public static readonly Error TaxIdIsInvalid = new("118");
    public static readonly Error LegalNameIsRequired = new("119");
    public static readonly Error CountryIsInvalid = new("120");
    public static readonly Error ProviderIsRequired = new("121");
    public static readonly Error DocumentStatusDoesNotAllowIt = new("122");
    public static readonly Error TestSetIdIsRequired = new("123");
    public static readonly Error NumberingRangeIsInvalid = new("124");
    public static readonly Error PrefixIsInvalid = new("125");
    public static readonly Error MandateVersionIsRequired = new("126");
    public static readonly Error TimeZoneIsInvalid = new("127");
    public static readonly Error OnlyRejectedDocumentsCanBeCorrected = new("128");
    public static readonly Error DocumentCannotPrecedeItself = new("129");
    public static readonly Error IssuerStatusDoesNotAllowIt = new("130");
    public static readonly Error AmountCannotBeNegative = new("131");
    public static readonly Error QuantityMustBePositive = new("132");
    public static readonly Error DueDateIsRequired = new("133");
    public static readonly Error PaymentMeansIsRequired = new("134");
    public static readonly Error EmailIsRequired = new("135");
    public static readonly Error PhoneIsRequired = new("136");
    public static readonly Error AddressIsRequired = new("137");
    public static readonly Error NumberingRangeNotValidYet = new("138");
    public static readonly Error QuotaPeriodIsInvalid = new("139");
    public static readonly Error BillingReferenceOnlyInNotes = new("140");
    public static readonly Error ProviderDocumentIdIsRequired = new("141");
    public static readonly Error CufeIsRequired = new("142");
    public static readonly Error NumberIsRequired = new("143");
    public static readonly Error LineIsIncomplete = new("144");
    public static readonly Error ContentIsRequired = new("145");
    public static readonly Error PrepaymentIsIncomplete = new("146");
    public static readonly Error IssuerIsRequired = new("147");
    public static readonly Error CredentialsAreRequired = new("148");
    public static readonly Error ProviderIssuerIdIsRequired = new("149");
    public static readonly Error SuspensionReasonIsRequired = new("150");
    public static readonly Error FilesAreRequired = new("151");
    public static readonly Error DueDateBeforeIssueDate = new("152");
    public static readonly Error TestProviderCannotGoToProduction = new("153");
    public static readonly Error TestProviderNotForThePlatform = new("154");
    public static readonly Error PointOfSaleIsRequired = new("155");
    public static readonly Error PointOfSaleOnlyInPointOfSaleDocuments = new("156");

    /// <summary>
    /// The identification of a type that carries a check digit came without its hyphen and its digit.
    /// Arguments: the number as written and the digit that belongs to it.
    /// </summary>
    public static readonly Error CheckDigitIsMissing = new("157");

    /// <summary>
    /// The check digit is not the one of the number.
    /// Arguments: the number, the digit that belongs to it and the digit that came.
    /// </summary>
    public static readonly Error CheckDigitDoesNotMatch = new("158");

    /// <summary>
    /// The identification of a type that carries a check digit is not digits only followed by a hyphen and one digit.
    /// </summary>
    public static readonly Error CheckDigitFormatIsInvalid = new("159");

    /// <summary>
    /// The number came without a hyphen and its last digit is the check digit of the ones before it: it may carry it attached or not.
    /// Arguments: what was written, what was written without its last digit, that last digit and the digit of everything written.
    /// </summary>
    public static readonly Error CheckDigitMayBeAttached = new("160");

    /// <summary>
    /// The aggregate received an identification without the rules of the check digit: a programming error.
    /// </summary>
    public static readonly Error CheckDigitRulesAreRequired = new("161");

    /// <summary>
    /// The issuer is kept with a tax id of a type that carries a check digit, without the digit that belongs to it: it
    /// issues nothing until its fiscal data are corrected. Argument: the tax id as it is kept.
    /// </summary>
    public static readonly Error IssuerTaxIdWithoutItsCheckDigit = new("162");
}
