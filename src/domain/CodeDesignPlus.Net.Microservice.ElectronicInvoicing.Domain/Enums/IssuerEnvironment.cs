namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// The environment of the tax authority the documents of an issuer are sent to.
/// </summary>
public enum IssuerEnvironment
{
    /// <summary>
    /// Tests without legal effects.
    /// </summary>
    Test = 1,

    /// <summary>
    /// The enablement tests, with the test set of the tax authority.
    /// </summary>
    Habilitation = 2,

    /// <summary>
    /// Documents with legal effects.
    /// </summary>
    Production = 3
}
