namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// Who the document is issued to, with the codes of the tax authority's lists. The caller decides every value.
/// </summary>
/// <param name="PersonTypeCode">Code of the type of person (natural or juridical) in the tax authority's list.</param>
/// <param name="IdentificationTypeCode">Code of the type of identification in the tax authority's list.</param>
/// <param name="IdentificationNumber">The identification number, without the check digit.</param>
/// <param name="CheckDigit">The check digit, when the type of identification has one.</param>
/// <param name="Name">Legal name or full name.</param>
/// <param name="FirstName">First names of a natural person.</param>
/// <param name="LastName">Last names of a natural person.</param>
/// <param name="Email">Where the provider delivers the document.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="TaxLiabilityCode">Code of the tax the buyer is liable for (for example <c>ZZ</c>).</param>
/// <param name="TaxResponsibilities">Codes of the fiscal responsibilities of the buyer (for example <c>R-99-PN</c>).</param>
/// <param name="Address">The address of the buyer, when it is known.</param>
public sealed record Buyer(string PersonTypeCode, string IdentificationTypeCode, string IdentificationNumber, string? CheckDigit, string Name, string? FirstName, string? LastName, string? Email, string? Phone, string? TaxLiabilityCode, List<string> TaxResponsibilities, PostalAddress? Address)
{
    /// <summary>
    /// The buyer in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(PersonTypeCode, IdentificationTypeCode, IdentificationNumber, CheckDigit, Name, FirstName, LastName, Email, Phone, TaxLiabilityCode, Canonicalizer.List(TaxResponsibilities), Address?.Canonical());
}
