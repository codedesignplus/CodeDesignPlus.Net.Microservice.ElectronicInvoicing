namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// Who assigns the consecutive numbers of a numbering range.
/// </summary>
public enum NumberingSource
{
    /// <summary>
    /// This service reserves each number before sending.
    /// </summary>
    Platform = 1,

    /// <summary>
    /// The provider numbers; the range here is an informative mirror and is not consumed.
    /// </summary>
    Provider = 2
}
