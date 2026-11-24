namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// How much of the provider's plan the deployment used in a calendar month: the documents of every issuer count against
/// the same plan, which includes a number of documents each month that do not carry over to the next one.
/// </summary>
/// <remarks>
/// One per month, with an id derived from the provider and the start of the month. Only the watch job writes it, from a
/// count of the documents; it remembers the thresholds already announced so each is announced once in the month. It
/// belongs to the platform's scope (<see cref="PlatformScope"/>). It does not block anything: the documents over the
/// included ones are issued and the provider bills them, so the month only says how many went over and what they are
/// estimated to cost. The quota of each tenant (<see cref="DocumentQuotaAggregate"/>) is what blocks.
/// </remarks>
public class ProviderPlanUsageAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <summary>
    /// Namespace of the ids derived from the plan. Changing it would orphan every stored usage.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("5e8d2c71-3a94-4b6f-b0e2-9c1f47a8d356");

    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// The tenant reserved for the platform's scope (<see cref="PlatformScope.Tenant"/>).
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; private set; } = null!;

    /// <summary>
    /// The first day of the month, in the time zone of the plan.
    /// </summary>
    public LocalDate Month { get; private set; }

    /// <summary>
    /// When the counted period starts: the start of the month, or of the contract when it started in the month.
    /// </summary>
    public Instant PeriodStart { get; private set; }

    /// <summary>
    /// When the counted period ends, exclusive: the start of the next month, or the end of the contract when it ends in the month.
    /// </summary>
    public Instant PeriodEnd { get; private set; }

    /// <summary>
    /// Documents the plan includes in the month.
    /// </summary>
    public long IncludedDocuments { get; private set; }

    /// <summary>
    /// Documents of the month, at the last count.
    /// </summary>
    public long Used { get; private set; }

    /// <summary>
    /// Documents of the month over the included ones.
    /// </summary>
    public long Overage { get; private set; }

    /// <summary>
    /// What the documents over the included ones are estimated to cost, in minor units of <see cref="Currency"/>.
    /// </summary>
    public long EstimatedOverageCost { get; private set; }

    /// <summary>
    /// ISO 4217 code of the currency of the plan.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// When the documents were last counted.
    /// </summary>
    public Instant? CountedAt { get; private set; }

    /// <summary>
    /// The thresholds already announced in the month, as percentages of the included documents.
    /// </summary>
    public List<int> AnnouncedPercentages { get; private set; } = [];

    /// <summary>
    /// Whether the end of the contract was announced from this month.
    /// </summary>
    public bool ExpiryAnnounced { get; private set; }

    /// <summary>
    /// The id of the usage of a month of a plan.
    /// </summary>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="periodStart">When the counted period of the month starts.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(string provider, Instant periodStart) => DeterministicGuid.Create(IdNamespace, $"{provider}|{periodStart.ToUnixTimeTicks()}");

    /// <summary>
    /// Starts the usage of a month of a plan, in the platform's scope (<see cref="PlatformScope"/>): the plan is the
    /// deployment's, not a tenant's.
    /// </summary>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="month">The first day of the month.</param>
    /// <param name="periodStart">When the counted period starts.</param>
    /// <param name="periodEnd">When the counted period ends, exclusive.</param>
    /// <param name="currency">ISO 4217 code of the currency of the plan.</param>
    /// <param name="createdBy">Who counts it.</param>
    /// <returns>The usage, not yet saved (version 0).</returns>
    public static ProviderPlanUsageAggregate Start(string provider, LocalDate month, Instant periodStart, Instant periodEnd, string currency, Guid createdBy)
    {
        DomainGuard.IsNullOrWhiteSpace(provider, Errors.ProviderIsRequired);
        DomainGuard.IsTrue(periodEnd <= periodStart || month.Day != 1, Errors.QuotaPeriodIsInvalid);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        return new ProviderPlanUsageAggregate(IdFor(provider, periodStart))
        {
            Tenant = PlatformScope.Tenant,
            Provider = provider,
            Month = month,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Currency = currency,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    /// <summary>
    /// Records a new count of the month, with its overage and its estimated cost, and announces the thresholds crossed
    /// and the end of the contract, each once.
    /// </summary>
    /// <param name="used">Documents of the month.</param>
    /// <param name="includedDocuments">Documents the plan includes in the month, as configured now.</param>
    /// <param name="tiers">The tiers of the price of the documents over the included ones, as configured now.</param>
    /// <param name="contractEnd">When the contract ends, as configured now.</param>
    /// <param name="alertPercentages">The thresholds, as percentages of the included documents, announced when they are crossed.</param>
    /// <param name="expiryNotice">How long before the end of the contract it is announced.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who counts it.</param>
    public void Record(long used, long includedDocuments, IReadOnlyList<OverageTier> tiers, Instant contractEnd, IEnumerable<int> alertPercentages, Duration expiryNotice, Instant now, Guid updatedBy)
    {
        DomainGuard.IsTrue(used < 0, Errors.AmountCannotBeNegative);
        DomainGuard.IsTrue(includedDocuments <= 0, Errors.QuotaPeriodIsInvalid);

        Used = used;
        IncludedDocuments = includedDocuments;
        Overage = Math.Max(0, used - includedDocuments);
        EstimatedOverageCost = OverageTier.CostOf(used, includedDocuments, tiers);
        CountedAt = now;

        foreach (var percentage in (alertPercentages ?? []).Where(x => x > 0).Order())
        {
            if (!AnnouncedPercentages.Contains(percentage) && Used * 100 >= IncludedDocuments * percentage)
            {
                AnnouncedPercentages.Add(percentage);

                AddEvent(new ProviderPlanThresholdReachedDomainEvent(Id, Provider, percentage, Used, IncludedDocuments, PeriodEnd, Month, Overage, EstimatedOverageCost, Currency));
            }
        }

        // The notice is announced from the month in which its window opens: a window that opened in an earlier month was
        // announced by that month's usage.
        var noticeOpens = contractEnd - expiryNotice;

        if (!ExpiryAnnounced && now < contractEnd && now >= noticeOpens && noticeOpens >= PeriodStart)
        {
            ExpiryAnnounced = true;

            AddEvent(new ProviderPlanExpiringDomainEvent(Id, Provider, Used, IncludedDocuments, contractEnd));
        }

        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
