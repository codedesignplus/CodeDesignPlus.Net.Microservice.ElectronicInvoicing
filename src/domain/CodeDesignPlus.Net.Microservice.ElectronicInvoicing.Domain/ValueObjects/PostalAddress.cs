namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// An address as the electronic document carries it, with the codes of the tax authority's lists.
/// </summary>
/// <param name="Street">The street address.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code.</param>
/// <param name="CountryName">Name of the country.</param>
/// <param name="RegionCode">Code of the region (department) in the tax authority's list.</param>
/// <param name="RegionName">Name of the region.</param>
/// <param name="CityCode">Code of the city in the tax authority's list (DANE in Colombia).</param>
/// <param name="CityName">Name of the city.</param>
/// <param name="PostalCode">Postal code, when known.</param>
public sealed record PostalAddress(string Street, string CountryCode, string CountryName, string RegionCode, string RegionName, string CityCode, string CityName, string? PostalCode)
{
    /// <summary>
    /// The address in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(Street, CountryCode, CountryName, RegionCode, RegionName, CityCode, CityName, PostalCode);
}
