namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// Who rejected an electronic document.
/// </summary>
public enum RejectionOrigin
{
    /// <summary>
    /// The provider refused it before reaching the tax authority (its own validation).
    /// </summary>
    Provider = 1,

    /// <summary>
    /// The tax authority rejected it.
    /// </summary>
    Dian = 2
}
