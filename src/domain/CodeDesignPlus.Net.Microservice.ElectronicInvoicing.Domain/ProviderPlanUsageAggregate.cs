namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// How much of the provider's plan the deployment has used: the documents of every issuer count against the same plan.
/// </summary>
/// <remarks>
/// One per plan period, with an id derived from the provider and the start of the contract. Only the watch job writes
/// it, from a count of the documents sent; it remembers the thresholds already announced so each is announced once.
/// It belongs to the platform's tenant. It does not block anything: selling more than the plan holds is warned to whoever
/// runs the platform.
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
    /// The platform's tenant.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// The provider of the plan.
    /// </summary>
    public string Provider { get; private set; } = null!;

    /// <summary>
    /// Documents the plan includes.
    /// </summary>
    public long Documents { get; private set; }

    /// <summary>
    /// When the plan started.
    /// </summary>
    public Instant ValidFrom { get; private set; }

    /// <summary>
    /// When the plan ends.
    /// </summary>
    public Instant ValidUntil { get; private set; }

    /// <summary>
    /// Documents sent in the plan's period, at the last count.
    /// </summary>
    public long Used { get; private set; }

    /// <summary>
    /// When the documents were last counted.
    /// </summary>
    public Instant? CountedAt { get; private set; }

    /// <summary>
    /// The thresholds already announced, as percentages of the plan.
    /// </summary>
    public List<int> AnnouncedPercentages { get; private set; } = [];

    /// <summary>
    /// Whether the end of the plan was already announced.
    /// </summary>
    public bool ExpiryAnnounced { get; private set; }

    /// <summary>
    /// The id of the usage of a plan.
    /// </summary>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="validFrom">When the plan started.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(string provider, Instant validFrom) => DeterministicGuid.Create(IdNamespace, $"{provider}|{validFrom.ToUnixTimeTicks()}");

    /// <summary>
    /// Starts the usage of a plan.
    /// </summary>
    /// <param name="tenant">The platform's tenant.</param>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="documents">Documents the plan includes.</param>
    /// <param name="validFrom">When the plan started.</param>
    /// <param name="validUntil">When the plan ends.</param>
    /// <param name="createdBy">Who counts it.</param>
    /// <returns>The usage, not yet saved (version 0).</returns>
    public static ProviderPlanUsageAggregate Start(Guid tenant, string provider, long documents, Instant validFrom, Instant validUntil, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.IsNullOrWhiteSpace(provider, Errors.ProviderIsRequired);
        DomainGuard.IsTrue(documents <= 0, Errors.QuotaPeriodIsInvalid);
        DomainGuard.IsTrue(validUntil <= validFrom, Errors.QuotaPeriodIsInvalid);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        return new ProviderPlanUsageAggregate(IdFor(provider, validFrom))
        {
            Tenant = tenant,
            Provider = provider,
            Documents = documents,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    /// <summary>
    /// Records a new count and announces the thresholds crossed and the end of the plan, each once.
    /// </summary>
    /// <param name="used">Documents sent in the plan's period.</param>
    /// <param name="documents">Documents the plan includes, as configured now.</param>
    /// <param name="validUntil">When the plan ends, as configured now.</param>
    /// <param name="alertPercentages">The thresholds announced when they are crossed.</param>
    /// <param name="expiryNotice">How long before the end of the plan it is announced.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who counts it.</param>
    public void Record(long used, long documents, Instant validUntil, IEnumerable<int> alertPercentages, Duration expiryNotice, Instant now, Guid updatedBy)
    {
        DomainGuard.IsTrue(used < 0, Errors.AmountCannotBeNegative);
        DomainGuard.IsTrue(documents <= 0 || validUntil <= ValidFrom, Errors.QuotaPeriodIsInvalid);

        Used = used;
        Documents = documents;
        ValidUntil = validUntil;
        CountedAt = now;

        foreach (var percentage in (alertPercentages ?? []).Where(x => x > 0).Order())
        {
            if (!AnnouncedPercentages.Contains(percentage) && Used * 100 >= Documents * percentage)
            {
                AnnouncedPercentages.Add(percentage);

                AddEvent(new ProviderPlanThresholdReachedDomainEvent(Id, Provider, percentage, Used, Documents, ValidUntil));
            }
        }

        if (!ExpiryAnnounced && now < ValidUntil && ValidUntil - now <= expiryNotice)
        {
            ExpiryAnnounced = true;

            AddEvent(new ProviderPlanExpiringDomainEvent(Id, Provider, Used, Documents, ValidUntil));
        }

        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
