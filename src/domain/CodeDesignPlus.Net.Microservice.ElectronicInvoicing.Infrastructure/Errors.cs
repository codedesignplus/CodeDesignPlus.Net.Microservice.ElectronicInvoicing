using CodeDesignPlus.Net.Exceptions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure;

/// <summary>
/// Error codes of the infrastructure and entry point layers; their messages live in <c>Resources/errors.{language}.json</c>.
/// </summary>
public class Errors : IErrorCodes
{
    public static readonly Error UnknownError = new("300");
    public static readonly Error ProviderUnavailable = new("301");
    public static readonly Error ProviderCredentialsRejected = new("302");
    public static readonly Error VaultUnavailable = new("303");
    public static readonly Error InvalidId = new("304");
    public static readonly Error SecretContextNotConfigured = new("305");
    public static readonly Error ProviderAnswerNotUnderstood = new("306");
    public static readonly Error InvalidDocumentType = new("307");
    public static readonly Error InvalidDate = new("312");
    public static readonly Error InvalidContent = new("313");
    public static readonly Error AliaddoNeedsNumberingResolution = new("309");
    public static readonly Error AliaddoNeedsNumericResolution = new("310");
    public static readonly Error AliaddoNeedsWholeQuantities = new("311");
    public static readonly Error PlatformTokenNotConfigured = new("308");
}
