using CodeDesignPlus.Net.Exceptions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure;

/// <summary>
/// Error codes of the infrastructure and entry point layers; their messages live in <c>Resources/errors.{language}.json</c>.
/// </summary>
public class Errors : IErrorCodes
{
    public static readonly Error UnknownError = new("300");
    public static readonly Error InvalidId = new("301");
}
