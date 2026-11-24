using System.Text.RegularExpressions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// The issuer of a tenant: its fiscal data as they go into the document, the provider it issues with and its
/// enablement before the tax authority.
/// </summary>
/// <remarks>
/// One per tenant, with an id derived from it (<see cref="IdFor"/>), so a repeated registration finds the same
/// document. The commands of the screen, the provisioning and the suspension after rejected credentials write the same
/// document, hence <see cref="IVersionedAggregate"/>.
/// </remarks>
public partial class IssuerAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <summary>
    /// Namespace of the ids derived from the tenant. Changing it would orphan every stored issuer.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("3f6c2b8e-51d4-4a0f-9e27-8b1d6c4a7e05");

    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// Tenant of the issuer.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country, which decides the providers the issuer can choose.
    /// </summary>
    public string Country { get; private set; } = null!;

    /// <summary>
    /// Code of the type of identification of the issuer in the tax authority's list (for example <c>31</c>, NIT).
    /// </summary>
    public string TaxIdTypeCode { get; private set; } = null!;

    /// <summary>
    /// Tax id as the tax authority writes it. When its type carries a check digit (<see cref="CheckDigitRules"/>), the
    /// number, a hyphen and the digit that belongs to it (<c>900000002-1</c>).
    /// </summary>
    public string TaxId { get; private set; } = null!;

    /// <summary>
    /// Legal name as it goes into the document.
    /// </summary>
    public string LegalName { get; private set; } = null!;

    /// <summary>
    /// Code of the type of person in the tax authority's list.
    /// </summary>
    public string PersonTypeCode { get; private set; } = null!;

    /// <summary>
    /// Code of the tax regime in the tax authority's list.
    /// </summary>
    public string TaxRegimeCode { get; private set; } = null!;

    /// <summary>
    /// Codes of the fiscal responsibilities of the issuer.
    /// </summary>
    public List<string> TaxResponsibilities { get; private set; } = [];

    /// <summary>
    /// The address of the issuer.
    /// </summary>
    public PostalAddress Address { get; private set; } = null!;

    /// <summary>
    /// The email of the issuer.
    /// </summary>
    public string Email { get; private set; } = null!;

    /// <summary>
    /// The phone of the issuer.
    /// </summary>
    public string Phone { get; private set; } = null!;

    /// <summary>
    /// IANA time zone of the issuer: the calendar dates of its resolutions and documents are read in it.
    /// </summary>
    public string TimeZoneId { get; private set; } = null!;

    /// <summary>
    /// Code of the adapter the issuer issues with.
    /// </summary>
    public string Provider { get; private set; } = null!;

    /// <summary>
    /// Id of the issuer in the provider, once it exists there.
    /// </summary>
    public string? ProviderIssuerId { get; private set; }

    /// <summary>
    /// The enablement of the issuer.
    /// </summary>
    public IssuerStatus Status { get; private set; }

    /// <summary>
    /// The environment its documents are sent to.
    /// </summary>
    public IssuerEnvironment Environment { get; private set; }

    /// <summary>
    /// The id of the test set the tax authority gave for the enablement tests.
    /// </summary>
    public string? TestSetId { get; private set; }

    /// <summary>
    /// The authorization of the issuer for this service to issue on its behalf; without it nothing is issued.
    /// </summary>
    public IssuerMandate? Mandate { get; private set; }

    /// <summary>
    /// How many times in a row the provider rejected the credentials since they last worked.
    /// </summary>
    public int ConsecutiveCredentialRejections { get; private set; }

    /// <summary>
    /// Code of the error catalog that says why the issuer was suspended, while it is.
    /// </summary>
    public string? SuspensionReasonCode { get; private set; }

    /// <summary>
    /// Whether the issuer can send documents now: enabled, or in the enablement tests with its test set, and with the
    /// mandate accepted.
    /// </summary>
    public bool CanIssue => Mandate is not null && (Status == IssuerStatus.Enabled || (Status == IssuerStatus.Testing && !string.IsNullOrWhiteSpace(TestSetId)));

    /// <summary>
    /// The id of the issuer of a tenant: always the same for the same tenant.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid tenant) => DeterministicGuid.Create(IdNamespace, tenant.ToString());

    /// <summary>
    /// Registers the issuer of a tenant, still unknown by the provider.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="data">The fiscal data of the issuer.</param>
    /// <param name="provider">The adapter it issues with.</param>
    /// <param name="createdBy">Who registered it.</param>
    /// <param name="checkDigitRules">Which types of identification carry a check digit and how it is computed.</param>
    /// <returns>The issuer in <see cref="IssuerStatus.Draft"/>, sending to <see cref="IssuerEnvironment.Test"/>.</returns>
    public static IssuerAggregate Register(Guid tenant, IssuerFiscalData data, string provider, Guid createdBy, CheckDigitRules checkDigitRules)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);
        DomainGuard.IsNullOrWhiteSpace(provider, Errors.ProviderIsRequired);

        var issuer = new IssuerAggregate(IdFor(tenant))
        {
            Tenant = tenant,
            Provider = provider,
            Status = IssuerStatus.Draft,
            Environment = IssuerEnvironment.Test,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        issuer.Apply(data, checkDigitRules);

        return issuer;
    }

    /// <summary>
    /// Replaces the fiscal data of the issuer.
    /// </summary>
    /// <param name="data">The new fiscal data.</param>
    /// <param name="updatedBy">Who changed them.</param>
    /// <param name="checkDigitRules">Which types of identification carry a check digit and how it is computed.</param>
    public void UpdateFiscalData(IssuerFiscalData data, Guid updatedBy, CheckDigitRules checkDigitRules)
    {
        Apply(data, checkDigitRules);
        Touch(updatedBy);
    }

    /// <summary>
    /// Changes the adapter the issuer issues with. Only before it exists in a provider: moving an enabled issuer to
    /// another provider is a migration (it has to repeat the enablement before the tax authority).
    /// </summary>
    /// <param name="provider">The new adapter.</param>
    /// <param name="updatedBy">Who changed it.</param>
    public void ChooseProvider(string provider, Guid updatedBy)
    {
        DomainGuard.IsNullOrWhiteSpace(provider, Errors.ProviderIsRequired);
        DomainGuard.IsTrue(Status != IssuerStatus.Draft, Errors.IssuerStatusDoesNotAllowIt);

        Provider = provider;
        ProviderIssuerId = null;

        Touch(updatedBy);
    }

    /// <summary>
    /// Records the mandate the issuer accepted.
    /// </summary>
    /// <param name="mandate">The accepted mandate.</param>
    /// <param name="updatedBy">Who recorded it.</param>
    public void AcceptMandate(IssuerMandate mandate, Guid updatedBy)
    {
        DomainGuard.IsNull(mandate, Errors.MandateVersionIsRequired);
        DomainGuard.IsNullOrWhiteSpace(mandate.Version, Errors.MandateVersionIsRequired);
        DomainGuard.GuidIsEmpty(mandate.AcceptedBy, Errors.CreatedByIsRequired);

        Mandate = mandate;

        Touch(updatedBy);
    }

    /// <summary>
    /// Records that the issuer exists in the provider.
    /// </summary>
    /// <param name="providerIssuerId">Its id in the provider.</param>
    /// <param name="updatedBy">Who provisioned it.</param>
    public void MarkProvisioned(string providerIssuerId, Guid updatedBy)
    {
        DomainGuard.IsNullOrWhiteSpace(providerIssuerId, Errors.ProviderIssuerIdIsRequired);
        DomainGuard.IsTrue(Status != IssuerStatus.Draft, Errors.IssuerStatusDoesNotAllowIt);

        ProviderIssuerId = providerIssuerId;
        Status = IssuerStatus.Provisioned;

        Touch(updatedBy);
    }

    /// <summary>
    /// Starts the enablement tests: the documents go with the test set of the tax authority.
    /// </summary>
    /// <param name="testSetId">The id of the test set.</param>
    /// <param name="updatedBy">Who started them.</param>
    public void StartTesting(string testSetId, Guid updatedBy)
    {
        DomainGuard.IsNullOrWhiteSpace(testSetId, Errors.TestSetIdIsRequired);
        DomainGuard.IsTrue(!Guid.TryParse(testSetId, out _), Errors.TestSetIdIsRequired);
        DomainGuard.IsNull(Mandate, Errors.MandateNotAccepted);
        DomainGuard.IsTrue(Status is IssuerStatus.Enabled or IssuerStatus.Suspended, Errors.IssuerStatusDoesNotAllowIt);

        TestSetId = testSetId;
        Status = IssuerStatus.Testing;
        Environment = IssuerEnvironment.Habilitation;

        Touch(updatedBy);
    }

    /// <summary>
    /// Enables the issuer to send documents to the given environment.
    /// </summary>
    /// <param name="environment">Where its documents go from now on.</param>
    /// <param name="testProvider">Whether its provider is a test one, which never reaches a tax authority: it cannot go to production.</param>
    /// <param name="updatedBy">Who enabled it.</param>
    public void Enable(IssuerEnvironment environment, bool testProvider, Guid updatedBy)
    {
        DomainGuard.IsNull(Mandate, Errors.MandateNotAccepted);
        DomainGuard.IsTrue(environment == IssuerEnvironment.Habilitation, Errors.IssuerStatusDoesNotAllowIt);
        DomainGuard.IsTrue(testProvider && environment == IssuerEnvironment.Production, Errors.TestProviderCannotGoToProduction);

        Status = IssuerStatus.Enabled;
        Environment = environment;
        SuspensionReasonCode = null;
        ConsecutiveCredentialRejections = 0;

        Touch(updatedBy);

        AddEvent(new IssuerEnabledDomainEvent(Id, Tenant, Provider, Environment));
    }

    /// <summary>
    /// Suspends the issuer: it does not issue until someone enables it again.
    /// </summary>
    /// <param name="reasonCode">Code of the error catalog that says why.</param>
    /// <param name="updatedBy">Who suspended it.</param>
    /// <returns>False when it was already suspended, so nothing is written.</returns>
    public bool Suspend(string reasonCode, Guid updatedBy)
    {
        DomainGuard.IsNullOrWhiteSpace(reasonCode, Errors.SuspensionReasonIsRequired);

        if (Status == IssuerStatus.Suspended)
        {
            return false;
        }

        Status = IssuerStatus.Suspended;
        SuspensionReasonCode = reasonCode;

        Touch(updatedBy);

        AddEvent(new IssuerSuspendedDomainEvent(Id, Tenant, Provider, reasonCode));

        return true;
    }

    /// <summary>
    /// Counts a rejection of the credentials by the provider; repeated, it suspends the issuer.
    /// </summary>
    /// <param name="threshold">How many rejections in a row suspend it.</param>
    /// <param name="reasonCode">Code of the error catalog of the rejection.</param>
    /// <param name="updatedBy">The system user.</param>
    public void RecordCredentialRejection(int threshold, string reasonCode, Guid updatedBy)
    {
        ConsecutiveCredentialRejections++;

        Touch(updatedBy);

        if (ConsecutiveCredentialRejections >= threshold)
        {
            Suspend(reasonCode, updatedBy);
        }
    }

    /// <summary>
    /// Forgets the rejections of the credentials after they worked.
    /// </summary>
    /// <returns>False when there was nothing to forget.</returns>
    public bool ResetCredentialRejections()
    {
        if (ConsecutiveCredentialRejections == 0)
        {
            return false;
        }

        ConsecutiveCredentialRejections = 0;

        return true;
    }

    /// <summary>
    /// Checks that the tax id the issuer is kept with has its check digit, when its type carries one. An issuer kept
    /// before the rule, or while its type was not configured, is only corrected by editing it: until then it issues
    /// nothing, because every document would go to the tax authority with an empty digit.
    /// </summary>
    /// <param name="checkDigitRules">Which types of identification carry a check digit and how it is computed.</param>
    /// <exception cref="CodeDesignPlus.Net.Exceptions.CodeDesignPlusException">Thrown with <c>162</c> when the tax id has not the check digit of its number.</exception>
    public void EnsureTaxIdCheckDigit(CheckDigitRules checkDigitRules)
    {
        DomainGuard.IsNull(checkDigitRules, Errors.CheckDigitRulesAreRequired);
        DomainGuard.IsFalse(checkDigitRules.IsSatisfiedBy(TaxIdTypeCode, TaxId), Errors.IssuerTaxIdWithoutItsCheckDigit.With(TaxId));
    }

    /// <summary>
    /// The tax id without its check digit, as some providers ask for it.
    /// </summary>
    /// <returns>The number and the check digit.</returns>
    public (string Number, string CheckDigit) SplitTaxId()
    {
        var parts = TaxId.Split('-');

        return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }

    /// <summary>
    /// Checks the fiscal data and keeps them. The tax id of a type that carries a check digit goes through the rules of
    /// the digit, whatever the provider of the issuer: an issuer kept without its digit would issue every document with
    /// a tax id the tax authority refuses.
    /// </summary>
    private void Apply(IssuerFiscalData data, CheckDigitRules checkDigitRules)
    {
        DomainGuard.IsNull(checkDigitRules, Errors.CheckDigitRulesAreRequired);
        DomainGuard.IsNull(data, Errors.LegalNameIsRequired);
        DomainGuard.IsTrue(string.IsNullOrWhiteSpace(data.Country) || data.Country.Length != 2, Errors.CountryIsInvalid);
        DomainGuard.IsNullOrWhiteSpace(data.TaxIdTypeCode, Errors.TaxIdIsInvalid);
        DomainGuard.IsNullOrWhiteSpace(data.TaxId, Errors.TaxIdIsInvalid);

        if (checkDigitRules.Applies(data.TaxIdTypeCode))
        {
            checkDigitRules.Ensure(data.TaxIdTypeCode, data.TaxId);
        }
        else
        {
            DomainGuard.IsFalse(TaxIdFormat().IsMatch(data.TaxId), Errors.TaxIdIsInvalid);
        }

        DomainGuard.IsNullOrWhiteSpace(data.LegalName, Errors.LegalNameIsRequired);
        DomainGuard.IsNullOrWhiteSpace(data.PersonTypeCode, Errors.LegalNameIsRequired);
        DomainGuard.IsNullOrWhiteSpace(data.TaxRegimeCode, Errors.LegalNameIsRequired);
        DomainGuard.IsNull(data.Address, Errors.AddressIsRequired);
        DomainGuard.IsNullOrWhiteSpace(data.Email, Errors.EmailIsRequired);
        DomainGuard.IsNullOrWhiteSpace(data.Phone, Errors.PhoneIsRequired);
        DomainGuard.IsTrue(string.IsNullOrWhiteSpace(data.TimeZoneId) || DateTimeZoneProviders.Tzdb.GetZoneOrNull(data.TimeZoneId) is null, Errors.TimeZoneIsInvalid);

        Country = data.Country.ToUpperInvariant();
        TaxIdTypeCode = data.TaxIdTypeCode;
        TaxId = data.TaxId.Trim();
        LegalName = data.LegalName;
        PersonTypeCode = data.PersonTypeCode;
        TaxRegimeCode = data.TaxRegimeCode;
        TaxResponsibilities = [.. data.TaxResponsibilities ?? []];
        Address = data.Address;
        Email = data.Email;
        Phone = data.Phone;
        TimeZoneId = data.TimeZoneId;
    }

    private void Touch(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
        UpdatedAt = SystemClock.Instance.GetCurrentInstant();
    }

    /// <summary>
    /// The shape of a tax id whose type carries no check digit by the rules of the deployment.
    /// </summary>
    [GeneratedRegex("^[0-9A-Za-z]{3,20}(-[0-9])?$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TaxIdFormat();
}

/// <summary>
/// The fiscal data of an issuer, as they go into the document.
/// </summary>
/// <param name="Country">ISO 3166-1 alpha-2 country.</param>
/// <param name="TaxIdTypeCode">Code of the type of identification in the tax authority's list.</param>
/// <param name="TaxId">Tax id; with its check digit after a hyphen when its type carries one.</param>
/// <param name="LegalName">Legal name.</param>
/// <param name="PersonTypeCode">Code of the type of person.</param>
/// <param name="TaxRegimeCode">Code of the tax regime.</param>
/// <param name="TaxResponsibilities">Codes of the fiscal responsibilities.</param>
/// <param name="Address">The address.</param>
/// <param name="Email">The email.</param>
/// <param name="Phone">The phone.</param>
/// <param name="TimeZoneId">IANA time zone of the issuer.</param>
public sealed record IssuerFiscalData(string Country, string TaxIdTypeCode, string TaxId, string LegalName, string PersonTypeCode, string TaxRegimeCode, List<string> TaxResponsibilities, PostalAddress Address, string Email, string Phone, string TimeZoneId);
