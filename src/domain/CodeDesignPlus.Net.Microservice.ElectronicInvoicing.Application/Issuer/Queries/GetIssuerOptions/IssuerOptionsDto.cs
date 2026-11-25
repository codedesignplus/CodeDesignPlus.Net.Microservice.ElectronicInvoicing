using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuerOptions;

/// <summary>
/// What the form of an issuer offers.
/// </summary>
public class IssuerOptionsDto
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country the lists belong to.
    /// </summary>
    public string Country { get; set; } = null!;

    /// <summary>
    /// The types of person.
    /// </summary>
    public List<IssuerOptionDto> PersonTypes { get; set; } = [];

    /// <summary>
    /// The tax regimes.
    /// </summary>
    public List<IssuerOptionDto> TaxRegimes { get; set; } = [];

    /// <summary>
    /// The fiscal responsibilities.
    /// </summary>
    public List<IssuerOptionDto> TaxResponsibilities { get; set; } = [];

    /// <summary>
    /// The version of the mandate in force: the one sent when the issuer accepts it.
    /// </summary>
    public string MandateVersion { get; set; } = null!;

    /// <summary>
    /// The providers the issuer of the scope may choose, the default first and after them the test ones when its tenant
    /// has the permission.
    /// </summary>
    public List<string> AvailableProviders { get; set; } = [];

    /// <summary>
    /// The ones of <see cref="AvailableProviders"/> that are test providers: they never reach a tax authority.
    /// </summary>
    public List<string> TestProviders { get; set; } = [];

    /// <summary>
    /// Codes of the fiscal responsibilities the issuer does not choose: the history of fiscal data managed from outside
    /// it decides them. Empty without history.
    /// </summary>
    public List<string> FiscalProfileResponsibilities { get; set; } = [];

    /// <summary>
    /// The tax regime and the managed responsibilities in force today by that history, so the form of a new issuer takes
    /// them; null without a version in force.
    /// </summary>
    public FiscalProfileSideDto? FiscalProfile { get; set; }

    /// <summary>
    /// The options of the form as the service shows them.
    /// </summary>
    /// <param name="country">The country as configured.</param>
    /// <param name="catalogs">Its lists.</param>
    /// <param name="mandateVersion">The version of the mandate in force.</param>
    /// <param name="availableProviders">The providers the issuer may choose.</param>
    /// <param name="testProviders">Which of them are test ones.</param>
    /// <returns>The DTO.</returns>
    public static IssuerOptionsDto From(string country, IssuerCatalogsOptions catalogs, string mandateVersion, IEnumerable<string> availableProviders, IEnumerable<string> testProviders) => new()
    {
        Country = country.ToUpperInvariant(),
        PersonTypes = IssuerOptionDto.From(catalogs.PersonTypes),
        TaxRegimes = IssuerOptionDto.From(catalogs.TaxRegimes),
        TaxResponsibilities = IssuerOptionDto.From(catalogs.TaxResponsibilities),
        MandateVersion = mandateVersion,
        AvailableProviders = [.. availableProviders],
        TestProviders = [.. testProviders]
    };
}

/// <summary>
/// An entry of a list of the tax authority.
/// </summary>
/// <param name="Code">The code, as it is sent in the fiscal data of the issuer.</param>
/// <param name="Name">The name, as the screen shows it.</param>
public sealed record IssuerOptionDto(string Code, string Name)
{
    /// <summary>
    /// The entries of a configured list, in their order.
    /// </summary>
    /// <param name="entries">The configured entries.</param>
    /// <returns>The entries as the service shows them.</returns>
    public static List<IssuerOptionDto> From(IEnumerable<CatalogEntryOptions> entries) => [.. entries.Select(x => new IssuerOptionDto(x.Code, x.Name))];
}
