namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// Why a numbering range is running out.
/// </summary>
public enum NumberingRangeAlert
{
    /// <summary>
    /// Few numbers are left.
    /// </summary>
    Numbers = 1,

    /// <summary>
    /// Its validity ends soon.
    /// </summary>
    Validity = 2
}
