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
    /// Transit key that encrypts the credentials of the issuers that bring their own. It is derived by context: the
    /// tenant in the context keeps one issuer's ciphertext from decrypting as another's.
    /// </summary>
    [Required]
    public string SecretsKeyName { get; set; } = null!;

    /// <summary>
    /// How long the opened credentials of an issuer stay in this process's memory.
    /// </summary>
    public TimeSpan SecretsCacheLifetime { get; set; } = TimeSpan.FromMinutes(10);

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
    /// The user that signs what this service does by itself (the consumers and the jobs): the product's system user.
    /// </summary>
    public Guid SystemUser { get; set; }

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

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Routing.Count == 0 || Routing.Any(x => x.Key.Length != 2 || x.Value is null || x.Value.Count == 0))
        {
            yield return new ValidationResult("ElectronicInvoicing:Routing needs at least one country (ISO 3166-1 alpha-2) with at least one provider.", [nameof(Routing)]);
        }

        if (SystemUser == Guid.Empty)
        {
            yield return new ValidationResult("ElectronicInvoicing:SystemUser is required.", [nameof(SystemUser)]);
        }

        if (SecretsCacheLifetime <= TimeSpan.Zero)
        {
            yield return new ValidationResult("ElectronicInvoicing:SecretsCacheLifetime must be greater than zero.", [nameof(SecretsCacheLifetime)]);
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
/// The plan contracted with the provider.
/// </summary>
public class ProviderPlanOptions
{
    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// Documents the plan includes for its whole period.
    /// </summary>
    public long Documents { get; set; }

    /// <summary>
    /// When the plan started.
    /// </summary>
    public DateTimeOffset ValidFrom { get; set; }

    /// <summary>
    /// When the plan ends.
    /// </summary>
    public DateTimeOffset ValidUntil { get; set; }

    /// <summary>
    /// The percentages of the plan whose crossing is announced.
    /// </summary>
    public List<int> AlertPercentages { get; set; } = [];

    /// <summary>
    /// How long before the end of the plan it is announced.
    /// </summary>
    public TimeSpan ExpiryNotice { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// When the plan started, as an instant.
    /// </summary>
    public Instant From => Instant.FromDateTimeOffset(ValidFrom);

    /// <summary>
    /// When the plan ends, as an instant.
    /// </summary>
    public Instant Until => Instant.FromDateTimeOffset(ValidUntil);

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

        if (Documents <= 0)
        {
            yield return "ProviderPlan:Documents must be greater than zero.";
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
    }
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
