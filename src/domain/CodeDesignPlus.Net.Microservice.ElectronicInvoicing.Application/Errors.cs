using CodeDesignPlus.Net.Exceptions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application;

/// <summary>
/// Error codes of the application layer; their messages live in <c>Resources/errors.{language}.json</c>.
/// </summary>
public class Errors : IErrorCodes
{
    public static readonly Error UnknownError = new("200");
    public static readonly Error DocumentIdReusedWithOtherContent = new("201");
    public static readonly Error IssuerNotFound = new("202");
    public static readonly Error ProviderNotSupported = new("203");
    public static readonly Error InvalidRequest = new("204");
    public static readonly Error ElectronicDocumentNotFound = new("205");
    public static readonly Error BillingReferenceNotFound = new("206");
    public static readonly Error NoUsableNumberingRange = new("207");
    public static readonly Error ProviderCannotProvisionIssuers = new("208");
    public static readonly Error ProviderResolutionNotFound = new("209");
    public static readonly Error ExternalBillingReferenceNotSupported = new("210");
    public static readonly Error CurrencyNotFound = new("211");
    public static readonly Error ProviderNeedsNoCredentials = new("212");
    public static readonly Error PrecedingDocumentNotFound = new("213");
    public static readonly Error IssuerSuspendedByUser = new("215");
    public static readonly Error ProviderCredentialsNotAccepted = new("216");
    public static readonly Error HabilitationNotByApi = new("214");
    public static readonly Error TestSetRejected = new("217");
    public static readonly Error PlatformIssuerWithTenant = new("218");
    public static readonly Error TenantIsReserved = new("219");
}
