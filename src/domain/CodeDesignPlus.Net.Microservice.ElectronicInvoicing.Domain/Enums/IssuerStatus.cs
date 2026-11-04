namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// The enablement of an issuer to send electronic documents.
/// </summary>
public enum IssuerStatus
{
    /// <summary>
    /// Registered here, not yet known by the provider.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// It exists in the provider.
    /// </summary>
    Provisioned = 2,

    /// <summary>
    /// Sending the test set of the tax authority.
    /// </summary>
    Testing = 3,

    /// <summary>
    /// Enabled to issue with legal effects.
    /// </summary>
    Enabled = 4,

    /// <summary>
    /// Stopped: it does not issue until someone enables it again.
    /// </summary>
    Suspended = 5
}
