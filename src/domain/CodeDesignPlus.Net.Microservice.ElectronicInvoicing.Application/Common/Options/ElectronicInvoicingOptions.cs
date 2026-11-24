using System.ComponentModel.DataAnnotations;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;

/// <summary>
/// How this deployment issues electronic documents: which providers each country may use, the quota of the platform's
/// issuer, how the documents are retried and how the quotas are read. The platform's issuer is not configured: it is a
/// scope of its own (<see cref="PlatformScope"/>), not a tenant.
/// </summary>
/// <remarks>
/// Validated on start: a section that does not bind stops the pod instead of issuing with defaults nobody chose.
/// </remarks>
public class ElectronicInvoicingOptions : IValidatableObject
{
    /// <summary>
    /// Name of the section in the configuration.
    /// </summary>
    public static readonly string Section = "ElectronicInvoicing";

    /// <summary>
    /// The adapters an issuer may choose, by ISO 3166-1 alpha-2 country; the first one is the default. A country
    /// without an entry has no electronic invoicing.
    /// </summary>
    public Dictionary<string, List<string>> Routing { get; set; } = [];

    /// <summary>
    /// The adapters that never reach a tax authority. One that the routing of a country does not list can only be chosen,
    /// by its code, by the issuer of a tenant that was granted the permission (<see cref="TestProviderGrantAggregate"/>);
    /// an issuer with any of them never goes to production.
    /// </summary>
    public List<string> TestProviders { get; set; } = [];

    /// <summary>
    /// The code of the cash form of payment in the tax authority's list (in Colombia, <c>1</c>): a document in cash is due
    /// on the day of issue, which this service puts, and every other form needs the due date from the caller.
    /// </summary>
    public string CashPaymentForm { get; set; } = null!;

    /// <summary>
    /// The codes, in the tax authority's list, of the types of identification written with a hyphen and a check digit (in
    /// Colombia, <c>31</c>). The tax id of an issuer of one of these types is refused without the digit that belongs to
    /// it. Empty when no type carries one.
    /// </summary>
    public List<string> CheckDigitDocumentTypes { get; set; } = [];

    /// <summary>
    /// The codes of the types of identification of a buyer that are refused, when a document is received, without the
    /// check digit that belongs to their number. It is a list of its own so a deployment turns the check of the buyers
    /// on only when its callers send the digit; empty, no buyer is checked. The weights are the same.
    /// </summary>
    public List<string> BuyerCheckDigitDocumentTypes { get; set; } = [];

    /// <summary>
    /// The weights of the weighted modulus 11 the check digit is computed with, from the units digit to the left, as the
    /// tax authority fixes them.
    /// </summary>
    public List<int> CheckDigitWeights { get; set; } = [];

    /// <summary>
    /// The quota of the platform's issuer.
    /// </summary>
    public PlatformIssuerOptions PlatformIssuer { get; set; } = new();

    /// <summary>
    /// The plan contracted with the provider, shared by every issuer.
    /// </summary>
    public ProviderPlanOptions ProviderPlan { get; set; } = new();

    /// <summary>
    /// How the quota of each tenant is read from its license.
    /// </summary>
    public QuotaOptions Quota { get; set; } = new();

    /// <summary>
    /// How the documents are sent again and asked about.
    /// </summary>
    public StatusCheckOptions StatusCheck { get; set; } = new();

    /// <summary>
    /// When a numbering range is announced as running out.
    /// </summary>
    public NumberingAlertsOptions NumberingAlerts { get; set; } = new();

    /// <summary>
    /// How many rejections of the credentials in a row suspend an issuer. More than one, so a transient failure never
    /// stops an issuer.
    /// </summary>
    public int SuspendAfterCredentialRejections { get; set; } = 3;

    /// <summary>
    /// The folder of ms-filestorage where the XML and the PDF of the validated documents are kept.
    /// </summary>
    [Required]
    public string FilesTarget { get; set; } = null!;

    /// <summary>
    /// The template of ms-emails that carries the email a provider writes to deliver a validated document to its buyer
    /// (<see cref="Providers.IBuyerNoticeProvider"/>): one whose variables are <c>subject</c>, <c>title</c>,
    /// <c>content</c>, <c>organization_name</c> and <c>current_year</c>. A tenant may have a template of its own with the
    /// same name.
    /// </summary>
    [Required]
    public string BuyerNoticeEmailTemplate { get; set; } = "Notice";

    /// <summary>
    /// The user that signs what this service does by itself (the consumers and the jobs): the product's system user.
    /// </summary>
    public Guid SystemUser { get; set; }

    /// <summary>
    /// The rules of the check digit as the domain reads them.
    /// </summary>
    /// <returns>The configured types and weights.</returns>
    public CheckDigitRules ToCheckDigitRules() => new(CheckDigitDocumentTypes, CheckDigitWeights);

    /// <summary>
    /// The rules of the check digit of a buyer as the domain reads them.
    /// </summary>
    /// <returns>The types configured for the buyers and the weights.</returns>
    public CheckDigitRules ToBuyerCheckDigitRules() => new(BuyerCheckDigitDocumentTypes, CheckDigitWeights);

    /// <summary>
    /// The adapters an issuer of a country may choose.
    /// </summary>
    /// <param name="country">ISO 3166-1 alpha-2 code.</param>
    /// <returns>The adapters, the default first; empty when the country has none.</returns>
    public IReadOnlyList<string> ProvidersFor(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return [];
        }

        var route = Routing.FirstOrDefault(x => string.Equals(x.Key, country, StringComparison.OrdinalIgnoreCase));

        return route.Value?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
    }

    /// <summary>
    /// Whether an adapter is a test one: it never reaches a tax authority.
    /// </summary>
    /// <param name="provider">The code of the adapter.</param>
    /// <returns><c>true</c> when it is listed in <see cref="TestProviders"/>.</returns>
    public bool IsTestProvider(string? provider) => !string.IsNullOrWhiteSpace(provider) && TestProviders.Any(x => string.Equals(x, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether an issuer of a country needs the permission of its tenant to use an adapter: a test one that the routing of
    /// the country does not offer to everyone.
    /// </summary>
    /// <param name="country">ISO 3166-1 alpha-2 code.</param>
    /// <param name="provider">The code of the adapter.</param>
    /// <returns><c>true</c> when only a tenant with the permission may use it.</returns>
    public bool NeedsTestProviderGrant(string? country, string? provider) => IsTestProvider(provider) && !ProvidersFor(country).Any(x => string.Equals(x, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The adapters an issuer of a country may choose, with the test ones when its tenant has the permission.
    /// </summary>
    /// <param name="country">ISO 3166-1 alpha-2 code.</param>
    /// <param name="testProvidersGranted">Whether the tenant of the issuer may use the test providers.</param>
    /// <returns>The adapters of the routing, the default first, and after them the test ones the routing does not list.</returns>
    public IReadOnlyList<string> ProvidersFor(string? country, bool testProvidersGranted)
    {
        var routed = ProvidersFor(country);

        if (!testProvidersGranted)
        {
            return routed;
        }

        return [.. routed, .. TestProviders.Where(x => !string.IsNullOrWhiteSpace(x) && !routed.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Routing.Count == 0 || Routing.Any(x => x.Key.Length != 2 || x.Value is null || x.Value.Count == 0))
        {
            yield return new ValidationResult("ElectronicInvoicing:Routing needs at least one country (ISO 3166-1 alpha-2) with at least one provider.", [nameof(Routing)]);
        }

        if (TestProviders.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("ElectronicInvoicing:TestProviders cannot carry an empty code.", [nameof(TestProviders)]);
        }

        if (string.IsNullOrWhiteSpace(CashPaymentForm))
        {
            yield return new ValidationResult("ElectronicInvoicing:CashPaymentForm is required: it tells which documents are due on the day of issue.", [nameof(CashPaymentForm)]);
        }

        if (CheckDigitDocumentTypes.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("ElectronicInvoicing:CheckDigitDocumentTypes cannot carry an empty code.", [nameof(CheckDigitDocumentTypes)]);
        }

        if (BuyerCheckDigitDocumentTypes.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("ElectronicInvoicing:BuyerCheckDigitDocumentTypes cannot carry an empty code.", [nameof(BuyerCheckDigitDocumentTypes)]);
        }

        if (BuyerCheckDigitDocumentTypes.Count > 0 && CheckDigitWeights.Count == 0)
        {
            yield return new ValidationResult("ElectronicInvoicing:CheckDigitWeights is required when BuyerCheckDigitDocumentTypes has codes: without weights there is nothing to compute the check digit with.", [nameof(BuyerCheckDigitDocumentTypes)]);
        }

        if (CheckDigitDocumentTypes.Count > 0 && CheckDigitWeights.Count == 0)
        {
            yield return new ValidationResult("ElectronicInvoicing:CheckDigitWeights is required when CheckDigitDocumentTypes has codes: without weights there is nothing to compute the check digit with.", [nameof(CheckDigitWeights)]);
        }

        if (CheckDigitWeights.Any(x => x <= 0))
        {
            yield return new ValidationResult("ElectronicInvoicing:CheckDigitWeights must be positive: any other weight computes another digit.", [nameof(CheckDigitWeights)]);
        }

        if (SystemUser == Guid.Empty)
        {
            yield return new ValidationResult("ElectronicInvoicing:SystemUser is required.", [nameof(SystemUser)]);
        }

        if (SuspendAfterCredentialRejections <= 0)
        {
            yield return new ValidationResult("ElectronicInvoicing:SuspendAfterCredentialRejections must be greater than zero.", [nameof(SuspendAfterCredentialRejections)]);
        }

        if (string.Equals(PlatformIssuer.FilesTarget, FilesTarget, StringComparison.Ordinal))
        {
            yield return new ValidationResult("ElectronicInvoicing:PlatformIssuer:FilesTarget must differ from ElectronicInvoicing:FilesTarget: the platform's files are not kept in the folder of the tenants.", [nameof(PlatformIssuer)]);
        }

        foreach (var problem in PlatformIssuer.Problems().Concat(ProviderPlan.Problems()).Concat(Quota.Problems()).Concat(StatusCheck.Problems()).Concat(NumberingAlerts.Problems()))
        {
            yield return new ValidationResult($"ElectronicInvoicing:{problem}");
        }
    }
}

/// <summary>
/// The quota of the platform's issuer: a fixed number of documents for the period of the contract with the provider.
/// </summary>
public class PlatformIssuerOptions
{
    /// <summary>
    /// Documents of the provider's plan reserved for the platform's own sales.
    /// </summary>
    public long ReservedDocuments { get; set; }

    /// <summary>
    /// The folder of ms-filestorage where the files of the platform's documents are kept. It must be one of the platform
    /// folders of ms-filestorage (<c>FileScope:PlatformTargets</c>): the platform's issuer is not a tenant, so its files go
    /// to the container of the platform instead of the one of a tenant.
    /// </summary>
    public string FilesTarget { get; set; } = null!;

    /// <summary>
    /// What is wrong.
    /// </summary>
    /// <returns>One text per problem.</returns>
    public IEnumerable<string> Problems()
    {
        if (ReservedDocuments <= 0)
        {
            yield return "PlatformIssuer:ReservedDocuments must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(FilesTarget))
        {
            yield return "PlatformIssuer:FilesTarget is required.";
        }
    }
}

/// <summary>
/// The plan contracted with the provider, shared by every issuer: a number of documents included each calendar month,
/// which do not carry over, and a price by tiers for the documents a month goes over them.
/// </summary>
/// <remarks>
/// Going over the included documents never blocks an issue: the provider bills the overage. The plan is only watched,
/// to announce the thresholds and to estimate what the month will cost.
/// </remarks>
public class ProviderPlanOptions
{
    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// Documents the plan includes in each calendar month. What a month does not use is lost.
    /// </summary>
    public long DocumentsPerMonth { get; set; }

    /// <summary>
    /// IANA time zone in which the provider counts the calendar months.
    /// </summary>
    public string TimeZoneId { get; set; } = null!;

    /// <summary>
    /// When the contract started.
    /// </summary>
    public DateTimeOffset ValidFrom { get; set; }

    /// <summary>
    /// When the contract ends.
    /// </summary>
    public DateTimeOffset ValidUntil { get; set; }

    /// <summary>
    /// The percentages of the included documents of a month whose crossing is announced.
    /// </summary>
    public List<int> AlertPercentages { get; set; } = [];

    /// <summary>
    /// How long before the end of the contract it is announced.
    /// </summary>
    public TimeSpan ExpiryNotice { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// ISO 4217 code of the currency of the prices of the plan.
    /// </summary>
    public string Currency { get; set; } = null!;

    /// <summary>
    /// The tiers of the price of the documents over the included ones, in order; only the last one may have no end.
    /// </summary>
    public List<OverageTierOptions> OverageTiers { get; set; } = [];

    /// <summary>
    /// When the contract started, as an instant.
    /// </summary>
    public Instant From => Instant.FromDateTimeOffset(ValidFrom);

    /// <summary>
    /// When the contract ends, as an instant.
    /// </summary>
    public Instant Until => Instant.FromDateTimeOffset(ValidUntil);

    /// <summary>
    /// The tiers of the price as the domain prices them.
    /// </summary>
    /// <returns>The tiers, in order.</returns>
    public List<OverageTier> Tiers() => [.. OverageTiers.Select(x => new OverageTier(x.UpTo, x.UnitPrice))];

    /// <summary>
    /// The calendar month an instant falls in, in the time zone of the plan.
    /// </summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The first day of the month.</returns>
    public LocalDate MonthOf(Instant instant)
    {
        var day = instant.InZone(DateTimeZoneProviders.Tzdb[TimeZoneId]).Date;

        return new LocalDate(day.Year, day.Month, 1);
    }

    /// <summary>
    /// The period of a month that the contract covers: the month, cut at the start and the end of the contract.
    /// </summary>
    /// <param name="month">The first day of the month.</param>
    /// <returns>Its start and its exclusive end; null when the contract does not cover any of it.</returns>
    public (Instant Start, Instant End)? PeriodOf(LocalDate month)
    {
        var zone = DateTimeZoneProviders.Tzdb[TimeZoneId];
        var start = Instant.Max(month.AtStartOfDayInZone(zone).ToInstant(), From);
        var end = Instant.Min(month.PlusMonths(1).AtStartOfDayInZone(zone).ToInstant(), Until);

        if (end <= start)
        {
            return null;
        }

        return (start, end);
    }

    /// <summary>
    /// What is wrong.
    /// </summary>
    /// <returns>One text per problem.</returns>
    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            yield return "ProviderPlan:Provider is required.";
        }

        if (DocumentsPerMonth <= 0)
        {
            yield return "ProviderPlan:DocumentsPerMonth must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(TimeZoneId) || DateTimeZoneProviders.Tzdb.GetZoneOrNull(TimeZoneId) is null)
        {
            yield return "ProviderPlan:TimeZoneId must be an IANA time zone.";
        }

        if (ValidUntil <= ValidFrom)
        {
            yield return "ProviderPlan:ValidUntil must be after ValidFrom.";
        }

        if (AlertPercentages.Any(x => x <= 0 || x > 100))
        {
            yield return "ProviderPlan:AlertPercentages must be between 1 and 100.";
        }

        if (ExpiryNotice < TimeSpan.Zero)
        {
            yield return "ProviderPlan:ExpiryNotice cannot be negative.";
        }

        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3 || !Currency.All(char.IsAsciiLetterUpper))
        {
            yield return "ProviderPlan:Currency must be an ISO 4217 code.";
        }

        foreach (var problem in TierProblems())
        {
            yield return problem;
        }
    }

    private IEnumerable<string> TierProblems()
    {
        var previous = DocumentsPerMonth;

        for (var i = 0; i < OverageTiers.Count; i++)
        {
            var tier = OverageTiers[i];

            if (tier.UnitPrice < 0)
            {
                yield return $"ProviderPlan:OverageTiers:{i}:UnitPrice cannot be negative.";
            }

            if (tier.UpTo is null && i < OverageTiers.Count - 1)
            {
                yield return $"ProviderPlan:OverageTiers:{i}:UpTo is required: only the last tier may have no end.";
            }

            if (tier.UpTo is { } upTo)
            {
                if (upTo <= previous)
                {
                    yield return $"ProviderPlan:OverageTiers:{i}:UpTo must be greater than the included documents and than the previous tier.";
                }

                previous = upTo;
            }
        }
    }
}

/// <summary>
/// A tier of the price of the documents a month goes over the ones the plan includes.
/// </summary>
public class OverageTierOptions
{
    /// <summary>
    /// The last document of the month, counted from the first one, this tier prices; empty for the last tier.
    /// </summary>
    public long? UpTo { get; set; }

    /// <summary>
    /// The price of each document of the tier, in minor units of the currency of the plan (COP 14 is 1400).
    /// </summary>
    public long UnitPrice { get; set; }
}

/// <summary>
/// How the quota of each tenant is read from the attributes of its license.
/// </summary>
public class QuotaOptions
{
    /// <summary>
    /// The keys of the license attributes whose integer values add up to the quota. A key that is missing counts as zero.
    /// This service does not know what a package is: it reads these keys.
    /// </summary>
    public List<string> AttributeKeys { get; set; } = [];

    /// <summary>
    /// The percentages of the quota whose crossing is announced.
    /// </summary>
    public List<int> AlertPercentages { get; set; } = [];

    /// <summary>
    /// What is wrong.
    /// </summary>
    /// <returns>One text per problem.</returns>
    public IEnumerable<string> Problems()
    {
        if (AttributeKeys.Count == 0 || AttributeKeys.Any(string.IsNullOrWhiteSpace))
        {
            yield return "Quota:AttributeKeys needs at least one key.";
        }

        if (AlertPercentages.Any(x => x <= 0 || x > 100))
        {
            yield return "Quota:AlertPercentages must be between 1 and 100.";
        }
    }
}

/// <summary>
/// How the documents are sent again and asked about.
/// </summary>
public class StatusCheckOptions
{
    /// <summary>
    /// The wait after the first send or check.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How much the wait grows after each attempt.
    /// </summary>
    public double BackoffFactor { get; set; } = 2;

    /// <summary>
    /// The longest wait between two attempts.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How many attempts before the document is announced as stalled.
    /// </summary>
    public int MaxAttempts { get; set; } = 12;

    /// <summary>
    /// How long a sent document may wait for a final answer before it is announced as stalled.
    /// </summary>
    public TimeSpan SentTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How many documents the job looks at in a round.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long a round of sending or checking holds a document; longer than a call to the provider with its retries,
    /// so two processes never talk to the provider about the same document at once.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The wait after a given number of attempts: exponential, with a ceiling.
    /// </summary>
    /// <param name="attempts">The attempts made so far.</param>
    /// <returns>The wait.</returns>
    public Duration DelayAfter(int attempts)
    {
        var factor = Math.Pow(BackoffFactor, Math.Max(0, attempts - 1));
        var ticks = Math.Min(InitialDelay.Ticks * factor, MaxDelay.Ticks);

        return Duration.FromTimeSpan(TimeSpan.FromTicks((long)ticks));
    }

    /// <summary>
    /// What is wrong.
    /// </summary>
    /// <returns>One text per problem.</returns>
    public IEnumerable<string> Problems()
    {
        if (InitialDelay <= TimeSpan.Zero || MaxDelay < InitialDelay)
        {
            yield return "StatusCheck:InitialDelay must be greater than zero and MaxDelay not smaller than it.";
        }

        if (BackoffFactor < 1)
        {
            yield return "StatusCheck:BackoffFactor must be at least 1.";
        }

        if (MaxAttempts <= 0 || BatchSize <= 0)
        {
            yield return "StatusCheck:MaxAttempts and BatchSize must be greater than zero.";
        }

        if (SentTimeout <= TimeSpan.Zero)
        {
            yield return "StatusCheck:SentTimeout must be greater than zero.";
        }

        if (Lease <= TimeSpan.Zero)
        {
            yield return "StatusCheck:Lease must be greater than zero.";
        }
    }
}

/// <summary>
/// When a numbering range is announced as running out.
/// </summary>
public class NumberingAlertsOptions
{
    /// <summary>
    /// Below this percentage of numbers left, the numbers are running out.
    /// </summary>
    public int RemainingPercentage { get; set; } = 10;

    /// <summary>
    /// Fewer days than these before the end of the validity, the validity is running out.
    /// </summary>
    public int DaysBeforeExpiry { get; set; } = 30;

    /// <summary>
    /// What is wrong.
    /// </summary>
    /// <returns>One text per problem.</returns>
    public IEnumerable<string> Problems()
    {
        if (RemainingPercentage <= 0 || RemainingPercentage >= 100)
        {
            yield return "NumberingAlerts:RemainingPercentage must be between 1 and 99.";
        }

        if (DaysBeforeExpiry < 0)
        {
            yield return "NumberingAlerts:DaysBeforeExpiry cannot be negative.";
        }
    }
}
