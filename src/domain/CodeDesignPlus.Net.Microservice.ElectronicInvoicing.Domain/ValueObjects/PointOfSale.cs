namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// The cash register a point of sale ticket was sold at, as the tax authority asks for it in the ticket. This service does
/// not know what the register is (a store, a branch, a front desk): the caller says it, and each adapter takes it to the
/// fields of its provider.
/// </summary>
/// <remarks>
/// The subtotal of the sale that the tax authority asks for with the register is not kept here: it is the one of the
/// totals of the document (<see cref="DocumentTotals.Subtotal"/>), so the two cannot disagree.
/// </remarks>
/// <param name="RegisterCode">The inventory plate of the register.</param>
/// <param name="RegisterType">The type of register, as the caller names it.</param>
/// <param name="LocationName">The name of the place where the register is.</param>
/// <param name="LocationAddress">The address of that place.</param>
/// <param name="CashierName">First and last names of the cashier or seller.</param>
/// <param name="SaleCode">The code of the sale in the caller.</param>
public sealed record PointOfSale(string RegisterCode, string RegisterType, string LocationName, string LocationAddress, string CashierName, string SaleCode)
{
    /// <summary>
    /// Whether the six texts are there: the tax authority rejects a ticket that misses any of them.
    /// </summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(RegisterCode)
        && !string.IsNullOrWhiteSpace(RegisterType)
        && !string.IsNullOrWhiteSpace(LocationName)
        && !string.IsNullOrWhiteSpace(LocationAddress)
        && !string.IsNullOrWhiteSpace(CashierName)
        && !string.IsNullOrWhiteSpace(SaleCode);

    /// <summary>
    /// The register in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(RegisterCode, RegisterType, LocationName, LocationAddress, CashierName, SaleCode);
}
