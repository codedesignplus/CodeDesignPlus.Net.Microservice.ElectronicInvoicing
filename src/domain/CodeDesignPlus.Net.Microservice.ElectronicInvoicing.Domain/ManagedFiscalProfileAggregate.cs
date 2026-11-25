namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// The part of the fiscal data of the issuer of a tenant that another service manages: its tax regime and some of its
/// fiscal responsibilities, as a history where each version rules from a day.
/// </summary>
/// <remarks>
/// <para>
/// This service does not know why a tenant has a regime: the service that owns that fact (in Kappali, the fiscal profile
/// of ms-accounting) turns it into the codes of the tax authority and sends the whole history here. The issuer follows the
/// version in force today, in the time zone the history comes with, and the responsibilities it does not manage stay the
/// issuer's own. A version that rules from a later day is kept and applied when the day comes.
/// </para>
/// <para>
/// One per tenant, with an id derived from it, and it lives apart from the issuer because it can arrive before the issuer
/// is registered. Each push replaces the whole history: the sender always sends all of it, so there is nothing to merge.
/// </para>
/// </remarks>
/// <param name="id">The id, derived from the tenant (<see cref="IdFor"/>).</param>
public class ManagedFiscalProfileAggregate(Guid id) : AggregateRootBase(id)
{
    /// <summary>
    /// Namespace of the ids derived from the tenant. Changing it would orphan every stored history.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("5b9e3c71-2d84-4f6a-a1c0-7e52d8f19b36");

    /// <summary>
    /// Tenant of the issuer the history is for.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// IANA time zone the days of the history are read in: the one of the tenant.
    /// </summary>
    public string TimeZoneId { get; private set; } = null!;

    /// <summary>
    /// Codes of the fiscal responsibilities the history decides. The issuer does not choose them: it has the ones of the
    /// version in force, and keeps as its own every other one.
    /// </summary>
    public List<string> ManagedResponsibilities { get; private set; } = [];

    /// <summary>
    /// The versions, ordered by the day they rule from.
    /// </summary>
    public List<ManagedFiscalProfileEntry> Entries { get; private set; } = [];

    /// <summary>
    /// The id of the history of a tenant: always the same for the same tenant.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid tenant) => DeterministicGuid.Create(IdNamespace, tenant.ToString());

    /// <summary>
    /// The history of a tenant as its owner sends it.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="timeZoneId">IANA time zone of the tenant.</param>
    /// <param name="managedResponsibilities">The codes of the responsibilities the history decides.</param>
    /// <param name="entries">The versions.</param>
    /// <param name="createdBy">Who sends it: the system user.</param>
    /// <returns>The history, not yet saved.</returns>
    public static ManagedFiscalProfileAggregate Create(Guid tenant, string timeZoneId, IEnumerable<string> managedResponsibilities, IEnumerable<ManagedFiscalProfileEntry> entries, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.IsTrue(PlatformScope.Owns(tenant), Errors.ManagedFiscalProfileNotForThePlatform);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        var profile = new ManagedFiscalProfileAggregate(IdFor(tenant))
        {
            Tenant = tenant,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        profile.Apply(timeZoneId, managedResponsibilities, entries);

        return profile;
    }

    /// <summary>
    /// Replaces the history with the one its owner sends now.
    /// </summary>
    /// <param name="timeZoneId">IANA time zone of the tenant.</param>
    /// <param name="managedResponsibilities">The codes of the responsibilities the history decides.</param>
    /// <param name="entries">The versions.</param>
    /// <param name="updatedBy">Who sends it: the system user.</param>
    /// <returns>False when it is the history already kept, so nothing is written.</returns>
    public bool Replace(string timeZoneId, IEnumerable<string> managedResponsibilities, IEnumerable<ManagedFiscalProfileEntry> entries, Guid updatedBy)
    {
        var before = Canonical();

        Apply(timeZoneId, managedResponsibilities, entries);

        if (string.Equals(before, Canonical(), StringComparison.Ordinal))
        {
            return false;
        }

        UpdatedBy = updatedBy;
        UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        return true;
    }

    /// <summary>
    /// The version in force on a day: the one with the latest day that is not after it.
    /// </summary>
    /// <param name="date">The day, in the time zone of the tenant.</param>
    /// <returns>The version, or null when the history starts later.</returns>
    public ManagedFiscalProfileEntry? InForceOn(LocalDate date) => Entries.LastOrDefault(x => x.EffectiveFrom <= date);

    /// <summary>
    /// Today in the time zone of the tenant.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>The day.</returns>
    public LocalDate Today(Instant now) => now.InZone(DateTimeZoneProviders.Tzdb[TimeZoneId]).Date;

    /// <summary>
    /// The version in force today in the time zone of the tenant.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>The version, or null when the history starts later.</returns>
    public ManagedFiscalProfileEntry? InForceAt(Instant now) => InForceOn(Today(now));

    /// <summary>
    /// The responsibilities an issuer has with this history on a day: the ones it chose, without the managed ones, and
    /// the managed ones of the version in force.
    /// </summary>
    /// <param name="own">The responsibilities the issuer has or asks for.</param>
    /// <param name="entry">The version in force.</param>
    /// <param name="noneCode">The code that means «no responsibility» in the list of the country; null when the list has none.</param>
    /// <returns>The responsibilities, without repeating a code.</returns>
    public List<string> ResponsibilitiesWith(IEnumerable<string> own, ManagedFiscalProfileEntry entry, string? noneCode)
    {
        DomainGuard.IsNull(entry, Errors.ManagedFiscalProfileWithoutEntries);

        var chosen = (own ?? []).Where(x => !ManagedResponsibilities.Contains(x, StringComparer.OrdinalIgnoreCase));

        return FiscalResponsibilities.Normalize(chosen.Concat(entry.TaxResponsibilities), noneCode);
    }

    /// <summary>
    /// Checks the history and keeps it ordered by day.
    /// </summary>
    private void Apply(string timeZoneId, IEnumerable<string> managedResponsibilities, IEnumerable<ManagedFiscalProfileEntry> entries)
    {
        DomainGuard.IsTrue(string.IsNullOrWhiteSpace(timeZoneId) || DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) is null, Errors.TimeZoneIsInvalid);

        var managed = (managedResponsibilities ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var versions = (entries ?? []).ToList();

        DomainGuard.IsTrue(versions.Count == 0, Errors.ManagedFiscalProfileWithoutEntries);
        DomainGuard.IsTrue(versions.Exists(x => x is null || string.IsNullOrWhiteSpace(x.TaxRegimeCode)), Errors.ManagedEntryWithoutTaxRegime);
        DomainGuard.IsTrue(versions.GroupBy(x => x.EffectiveFrom).Any(x => x.Count() > 1), Errors.ManagedEntriesRepeatADay);

        foreach (var version in versions)
        {
            var foreign = (version.TaxResponsibilities ?? []).FirstOrDefault(x => !managed.Contains(x, StringComparer.OrdinalIgnoreCase));

            DomainGuard.IsTrue(foreign is not null, Errors.ManagedResponsibilityNotDeclared.With(version.EffectiveFrom.ToString("yyyy-MM-dd", null), foreign ?? string.Empty));
        }

        TimeZoneId = timeZoneId;
        ManagedResponsibilities = managed;
        Entries = [.. versions
            .OrderBy(x => x.EffectiveFrom)
            .Select(x => new ManagedFiscalProfileEntry(x.EffectiveFrom, x.TaxRegimeCode.Trim(), [.. (x.TaxResponsibilities ?? []).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)]))];
    }

    /// <summary>
    /// The history as one text, to tell whether a push changes it.
    /// </summary>
    private string Canonical() => string.Join('|', new[] { TimeZoneId ?? string.Empty, string.Join(',', ManagedResponsibilities.Order(StringComparer.OrdinalIgnoreCase)) }
        .Concat(Entries.Select(x => $"{x.EffectiveFrom:yyyy-MM-dd};{x.TaxRegimeCode};{string.Join(',', x.TaxResponsibilities.Order(StringComparer.OrdinalIgnoreCase))}")));
}

/// <summary>
/// A version of the fiscal data managed from outside: the regime and the managed responsibilities from a day on.
/// </summary>
/// <param name="EffectiveFrom">The day it rules from, in the time zone of the tenant.</param>
/// <param name="TaxRegimeCode">Code of the tax regime in the tax authority's list.</param>
/// <param name="TaxResponsibilities">Codes of the managed responsibilities the issuer has from that day; the managed ones not listed, it has not.</param>
public sealed record ManagedFiscalProfileEntry(LocalDate EffectiveFrom, string TaxRegimeCode, List<string> TaxResponsibilities);

/// <summary>
/// How the list of fiscal responsibilities of an issuer is written.
/// </summary>
public static class FiscalResponsibilities
{
    /// <summary>
    /// The responsibilities without repeating a code, in the order they came, with the code that means «no
    /// responsibility» only when there is no other: the tax authority does not take it next to a real one, and it takes no
    /// empty list.
    /// </summary>
    /// <param name="codes">The codes.</param>
    /// <param name="noneCode">The code that means «no responsibility» in the list of the country; null when the list has none, and then nothing is added or removed.</param>
    /// <returns>The codes as the issuer keeps them.</returns>
    public static List<string> Normalize(IEnumerable<string> codes, string? noneCode)
    {
        var distinct = (codes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (string.IsNullOrWhiteSpace(noneCode))
        {
            return distinct;
        }

        var real = distinct.Where(x => !string.Equals(x, noneCode, StringComparison.OrdinalIgnoreCase)).ToList();

        return real.Count == 0 ? [noneCode] : real;
    }

    /// <summary>
    /// Whether two lists carry the same codes, whatever their order.
    /// </summary>
    /// <param name="left">A list.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they carry the same codes.</returns>
    public static bool AreSame(IEnumerable<string>? left, IEnumerable<string>? right)
    {
        var a = new HashSet<string>(left ?? [], StringComparer.OrdinalIgnoreCase);

        return a.SetEquals(right ?? []);
    }
}
