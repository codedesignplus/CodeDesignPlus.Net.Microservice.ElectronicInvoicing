using CodeDesignPlus.Net.Exceptions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// Error codes of the domain layer; their messages live in <c>Resources/errors.{language}.json</c>.
/// </summary>
public class Errors : IErrorCodes
{
    public static readonly Error UnknownError = new("100");
    public static readonly Error ElectronicDocumentIdIsRequired = new("101");
    public static readonly Error TenantIsRequired = new("102");
    public static readonly Error ReferenceIsRequired = new("103");
    public static readonly Error ReferenceIsTooLong = new("104");
    public static readonly Error CreatedByIsRequired = new("105");
}
