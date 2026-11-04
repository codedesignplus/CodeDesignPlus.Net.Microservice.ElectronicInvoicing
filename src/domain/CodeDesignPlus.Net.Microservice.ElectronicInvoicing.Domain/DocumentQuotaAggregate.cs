namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// How many documents an issuer may send in its current period, and how many it has used.
/// </summary>
/// <remarks>
/// <para>
/// It lives apart from the issuer because the allowance can arrive before the issuer is registered: the tenant buys it
/// and enables the invoicing afterwards. One per tenant, with an id derived from it.
/// </para>
/// <para>
/// The period is not monthly: it lasts until the date the allowance says (the end of the tenant's license, or the end
/// of the contract with the provider for the platform's issuer), and the unused documents are lost when it ends. The
/// <see cref="Limit"/> is always the last value received, whatever the period: the service that sells the allowance
/// publishes it again whenever it changes. When the allowance is renewed before the period ends, the new period starts
/// where the current one ends (<see cref="RenewedUntil"/>), and the count starts again from zero then.
/// </para>
/// <para>
/// The invoices and the tickets need room in the period; the notes always go (correcting is a legal obligation) and are
/// counted too. A rejected document corrected on the same number is counted once: it is counted when it is created.
/// </para>
/// <para>
/// Every document created reserves its unit on this same document, hence <see cref="IVersionedAggregate"/>.
/// </para>
/// </remarks>
public class DocumentQuotaAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <summary>
    /// Namespace of the ids derived from the tenant. Changing it would orphan every stored quota.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("c47a1e93-6b2d-4f85-8e10-5d9b3a2f7c61");

    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// Tenant of the issuer.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// Documents allowed in the period.
    /// </summary>
    public long Limit { get; private set; }

    /// <summary>
    /// Documents used in the period.
    /// </summary>
    public long Used { get; private set; }

    /// <summary>
    /// When the current period started; null while no allowance has arrived.
    /// </summary>
    public Instant? PeriodStart { get; private set; }

    /// <summary>
    /// When the current period ends; null while no allowance has arrived.
    /// </summary>
    public Instant? PeriodEnd { get; private set; }

    /// <summary>
    /// The end of the next period, when the allowance was renewed before the current one ended.
    /// </summary>
    public Instant? RenewedUntil { get; private set; }

    /// <summary>
    /// When the allowance applied last was published, so an older one that arrives late does not undo it.
    /// </summary>
    public Instant? AllowanceAt { get; private set; }

    /// <summary>
    /// The thresholds already announced in the period, as percentages of the limit.
    /// </summary>
    public List<int> AnnouncedPercentages { get; private set; } = [];

    /// <summary>
    /// The id of the quota of a tenant: always the same for the same tenant.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid tenant) => DeterministicGuid.Create(IdNamespace, tenant.ToString());

    /// <summary>
    /// A quota without allowance: nothing but notes can be sent until one arrives.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="createdBy">Who caused it.</param>
    /// <returns>The empty quota, not yet saved (version 0).</returns>
    public static DocumentQuotaAggregate CreateEmpty(Guid tenant, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        return new DocumentQuotaAggregate(IdFor(tenant))
        {
            Tenant = tenant,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    /// <summary>
    /// Applies the allowance of a tenant as its license says it.
    /// </summary>
    /// <param name="limit">Documents allowed.</param>
    /// <param name="until">When the allowance ends (the end of the license).</param>
    /// <param name="publishedAt">When the allowance was published.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who applies it.</param>
    /// <returns>False when it was older than the one applied or changed nothing.</returns>
    public bool ApplyAllowance(long limit, Instant until, Instant publishedAt, Instant now, Guid updatedBy)
    {
        DomainGuard.IsTrue(limit < 0, Errors.AmountCannotBeNegative);

        if (AllowanceAt is not null && publishedAt < AllowanceAt)
        {
            return false;
        }

        var before = (Limit, PeriodStart, PeriodEnd, RenewedUntil);

        AllowanceAt = publishedAt;
        Limit = limit;

        if (PeriodEnd is null)
        {
            // An allowance that already ended starts an empty period: nothing but notes can be sent.
            StartPeriod(now < until ? now : until, until);
        }
        else if (until > PeriodEnd)
        {
            RenewedUntil = until;
        }
        else
        {
            PeriodEnd = until;
            RenewedUntil = null;
        }

        Roll(now);
        Forget();
        Touch(updatedBy, now);

        return before != (Limit, PeriodStart, PeriodEnd, RenewedUntil);
    }

    /// <summary>
    /// Applies the fixed allowance of the platform's issuer, which lasts as long as the contract with the provider.
    /// </summary>
    /// <param name="limit">Documents reserved for the platform.</param>
    /// <param name="from">When the contract started.</param>
    /// <param name="until">When the contract ends.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who applies it.</param>
    public void ApplyPlatformAllowance(long limit, Instant from, Instant until, Instant now, Guid updatedBy)
    {
        DomainGuard.IsTrue(limit < 0, Errors.AmountCannotBeNegative);
        DomainGuard.IsTrue(until <= from, Errors.QuotaPeriodIsInvalid);

        Limit = limit;
        RenewedUntil = null;

        // A new contract is a new count; an end corrected in the configuration of the same contract is not.
        if (PeriodStart != from)
        {
            StartPeriod(from, until);
        }
        else
        {
            PeriodEnd = until;
        }

        Forget();
        Touch(updatedBy, now);
    }

    /// <summary>
    /// The documents still allowed now.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>Zero when the period ended or there is no allowance.</returns>
    public long RemainingAt(Instant now)
    {
        var (start, end, used) = PeriodAt(now);

        if (start is null || end is null || now >= end || now < start)
        {
            return 0;
        }

        return Math.Max(0, Limit - used);
    }

    /// <summary>
    /// When the period that applies now ends.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>The end, or null when there is no allowance.</returns>
    public Instant? PeriodEndAt(Instant now) => PeriodAt(now).End;

    /// <summary>
    /// Counts one document.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="needsRoom">Whether the document needs room in the period (invoices and tickets do, notes do not).</param>
    /// <param name="alertPercentages">The thresholds that are announced when they are crossed.</param>
    /// <param name="updatedBy">Who causes it.</param>
    public void Reserve(Instant now, bool needsRoom, IEnumerable<int> alertPercentages, Guid updatedBy)
    {
        Roll(now);

        if (needsRoom)
        {
            DomainGuard.IsTrue(RemainingAt(now) <= 0, Errors.QuotaExhausted);
        }

        Used++;

        foreach (var percentage in (alertPercentages ?? []).Where(x => x > 0).Order())
        {
            if (!AnnouncedPercentages.Contains(percentage) && Limit > 0 && Used * 100 >= Limit * percentage)
            {
                AnnouncedPercentages.Add(percentage);

                AddEvent(new DocumentQuotaThresholdReachedDomainEvent(Id, Tenant, percentage, Used, Limit, PeriodEnd!.Value));
            }
        }

        Touch(updatedBy, now);
    }

    /// <summary>
    /// Gives back the unit of a document that was not created after all.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who causes it.</param>
    /// <returns>False when there was nothing to give back.</returns>
    public bool Release(Instant now, Guid updatedBy)
    {
        if (Used == 0)
        {
            return false;
        }

        Used--;

        Touch(updatedBy, now);

        return true;
    }

    private (Instant? Start, Instant? End, long Used) PeriodAt(Instant now)
    {
        if (PeriodEnd is not null && now >= PeriodEnd && RenewedUntil > PeriodEnd)
        {
            return (PeriodEnd, RenewedUntil, 0);
        }

        return (PeriodStart, PeriodEnd, Used);
    }

    private void Roll(Instant now)
    {
        if (PeriodEnd is not null && now >= PeriodEnd && RenewedUntil > PeriodEnd)
        {
            StartPeriod(PeriodEnd.Value, RenewedUntil.Value);
            RenewedUntil = null;
        }
    }

    private void StartPeriod(Instant start, Instant end)
    {
        DomainGuard.IsTrue(end < start, Errors.QuotaPeriodIsInvalid);

        PeriodStart = start;
        PeriodEnd = end;
        Used = 0;
        AnnouncedPercentages = [];
    }

    // A limit that grows leaves behind the thresholds that are no longer crossed, so they are announced again.
    private void Forget() => AnnouncedPercentages = [.. AnnouncedPercentages.Where(x => Limit > 0 && Used * 100 >= Limit * x)];

    private void Touch(Guid updatedBy, Instant now)
    {
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
