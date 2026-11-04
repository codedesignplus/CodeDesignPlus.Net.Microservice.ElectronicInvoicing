namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// Where an electronic document stands in its life against the provider and the tax authority.
/// </summary>
public enum ElectronicDocumentStatus
{
    /// <summary>
    /// Created and waiting to be sent, or corrected after a rejection.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// The provider received it and the answer of the tax authority is pending.
    /// </summary>
    Sent = 2,

    /// <summary>
    /// The tax authority validated it: it has its CUFE and is final.
    /// </summary>
    Validated = 3,

    /// <summary>
    /// The provider or the tax authority rejected it; the caller may correct it on the same number.
    /// </summary>
    Rejected = 4
}
