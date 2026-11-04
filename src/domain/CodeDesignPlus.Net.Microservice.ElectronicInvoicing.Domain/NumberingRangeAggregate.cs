namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// A numbering resolution of the tax authority for an issuer and a type of document: its prefix, its range, its
/// validity and the next consecutive.
/// </summary>
/// <remarks>
/// Two documents created at once reserve their numbers on the same range, hence <see cref="IVersionedAggregate"/>: the
/// second one conflicts, reads again and takes the next number, so no number is given twice. When the provider numbers,
/// the range is an informative mirror and is never consumed.
/// <para>
/// Its id derives from the issuer, the type, the prefix and the resolution, so synchronizing it again from the provider
/// finds the same range.
/// </para>
/// </remarks>
public class NumberingRangeAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <summary>
    /// Namespace of the ids derived from the resolution. Changing it would orphan every stored range.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("9b2e47d1-0c6f-4e38-a5d2-71f8e3b6c914");

    /// <summary>
    /// The longest prefix kept.
    /// </summary>
    private const int MaxPrefixLength = 10;

    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// Tenant of the issuer.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; private set; }

    /// <summary>
    /// The type of document the range numbers.
    /// </summary>
    public ElectronicDocumentType DocumentType { get; private set; }

    /// <summary>
    /// The number of the resolution.
    /// </summary>
    public string ResolutionNumber { get; private set; } = null!;

    /// <summary>
    /// The technical key of the resolution, which goes into the CUFE.
    /// </summary>
    public string? TechnicalKey { get; private set; }

    /// <summary>
    /// The prefix authorized.
    /// </summary>
    public string Prefix { get; private set; } = null!;

    /// <summary>
    /// The first number authorized.
    /// </summary>
    public long From { get; private set; }

    /// <summary>
    /// The last number authorized.
    /// </summary>
    public long To { get; private set; }

    /// <summary>
    /// The first day of the validity, in the time zone of the issuer.
    /// </summary>
    public LocalDate ValidFrom { get; private set; }

    /// <summary>
    /// The last day of the validity, in the time zone of the issuer.
    /// </summary>
    public LocalDate ValidUntil { get; private set; }

    /// <summary>
    /// The next number to give.
    /// </summary>
    public long Next { get; private set; }

    /// <summary>
    /// Who gives the numbers.
    /// </summary>
    public NumberingSource Source { get; private set; }

    /// <summary>
    /// The alerts already given, so each one is given once.
    /// </summary>
    public List<NumberingRangeAlert> Alerts { get; private set; } = [];

    /// <summary>
    /// How many numbers are left.
    /// </summary>
    public long Remaining => Math.Max(0, To - Next + 1);

    /// <summary>
    /// The id of a resolution of an issuer: always the same for the same issuer, type, prefix and resolution.
    /// </summary>
    /// <param name="issuerId">The id of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="resolutionNumber">The number of the resolution.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid issuerId, ElectronicDocumentType type, string prefix, string resolutionNumber) => DeterministicGuid.Create(IdNamespace, $"{issuerId}|{type}|{prefix}|{resolutionNumber}");

    /// <summary>
    /// Registers a resolution of an issuer.
    /// </summary>
    /// <param name="tenant">Tenant of the issuer.</param>
    /// <param name="issuerId">The id of the issuer.</param>
    /// <param name="data">The resolution.</param>
    /// <param name="source">Who gives the numbers.</param>
    /// <param name="createdBy">Who registered it.</param>
    /// <returns>The range, starting at its first number.</returns>
    public static NumberingRangeAggregate Create(Guid tenant, Guid issuerId, NumberingResolution data, NumberingSource source, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.GuidIsEmpty(issuerId, Errors.IssuerIsRequired);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);
        EnsureValid(data);

        return new NumberingRangeAggregate(IdFor(issuerId, data.DocumentType, data.Prefix, data.ResolutionNumber))
        {
            Tenant = tenant,
            IssuerId = issuerId,
            DocumentType = data.DocumentType,
            ResolutionNumber = data.ResolutionNumber,
            TechnicalKey = data.TechnicalKey,
            Prefix = data.Prefix,
            From = data.From,
            To = data.To,
            ValidFrom = data.ValidFrom,
            ValidUntil = data.ValidUntil,
            Next = data.From,
            Source = source,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    /// <summary>
    /// Refreshes the resolution with what the provider says now, keeping the next number.
    /// </summary>
    /// <param name="data">The resolution as the provider has it.</param>
    /// <param name="updatedBy">Who synchronized it.</param>
    /// <returns>False when nothing changed.</returns>
    public bool Refresh(NumberingResolution data, Guid updatedBy)
    {
        EnsureValid(data);

        if (TechnicalKey == data.TechnicalKey && From == data.From && To == data.To && ValidFrom == data.ValidFrom && ValidUntil == data.ValidUntil)
        {
            return false;
        }

        TechnicalKey = data.TechnicalKey;
        From = data.From;
        To = data.To;
        ValidFrom = data.ValidFrom;
        ValidUntil = data.ValidUntil;
        Next = Math.Max(Next, data.From);

        // A range that grows or is extended may need its alerts again when it runs out once more.
        Alerts = [];

        UpdatedBy = updatedBy;
        UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        return true;
    }

    /// <summary>
    /// Whether the range can give a number on a day.
    /// </summary>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <returns>True when it is valid that day and has numbers left.</returns>
    public bool IsUsableOn(LocalDate today) => IsActive && Source == NumberingSource.Platform && today >= ValidFrom && today <= ValidUntil && Remaining > 0;

    /// <summary>
    /// Gives the next number.
    /// </summary>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <returns>The number given.</returns>
    public DocumentNumber Reserve(LocalDate today)
    {
        DomainGuard.IsTrue(Source != NumberingSource.Platform, Errors.NumberingRangeIsInvalid);
        DomainGuard.IsTrue(today > ValidUntil, Errors.NumberingRangeExpired);
        DomainGuard.IsTrue(today < ValidFrom, Errors.NumberingRangeNotValidYet);
        DomainGuard.IsTrue(Remaining <= 0, Errors.NumberingRangeExhausted);

        var number = new DocumentNumber(Prefix, Next);

        Next++;

        return number;
    }

    /// <summary>
    /// Raises the alerts of a range that is running out, each one once.
    /// </summary>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <param name="remainingPercentage">Below this percentage of numbers left, the numbers are running out.</param>
    /// <param name="daysBeforeExpiry">Fewer days than these before the end of the validity, the validity is running out.</param>
    /// <returns>True when an alert was raised.</returns>
    public bool CheckRunningOut(LocalDate today, int remainingPercentage, int daysBeforeExpiry)
    {
        if (!IsActive || today > ValidUntil)
        {
            return false;
        }

        var raised = false;
        var size = To - From + 1;

        if (Source == NumberingSource.Platform && !Alerts.Contains(NumberingRangeAlert.Numbers) && Remaining * 100 <= size * remainingPercentage)
        {
            raised |= Raise(NumberingRangeAlert.Numbers);
        }

        if (!Alerts.Contains(NumberingRangeAlert.Validity) && Period.Between(today, ValidUntil, PeriodUnits.Days).Days <= daysBeforeExpiry)
        {
            raised |= Raise(NumberingRangeAlert.Validity);
        }

        return raised;
    }

    private bool Raise(NumberingRangeAlert alert)
    {
        Alerts.Add(alert);

        AddEvent(new NumberingRangeRunningOutDomainEvent(Id, Tenant, IssuerId, DocumentType, Prefix, alert, Remaining, ValidUntil));

        return true;
    }

    private static void EnsureValid(NumberingResolution data)
    {
        DomainGuard.IsNull(data, Errors.NumberingRangeIsInvalid);
        DomainGuard.IsFalse(Enum.IsDefined(data.DocumentType), Errors.DocumentTypeNotSupported);
        DomainGuard.IsNullOrWhiteSpace(data.ResolutionNumber, Errors.NumberingRangeIsInvalid);
        // The length only keeps the stored value sane; what a prefix may hold is the tax authority's rule, checked by the provider.
        DomainGuard.IsTrue(data.Prefix is null || data.Prefix.Length > MaxPrefixLength || data.Prefix.Any(x => !char.IsLetterOrDigit(x)), Errors.PrefixIsInvalid);
        DomainGuard.IsTrue(data.From < 0 || data.To < data.From, Errors.NumberingRangeIsInvalid);
        DomainGuard.IsTrue(data.ValidUntil < data.ValidFrom, Errors.NumberingRangeIsInvalid);
    }
}

/// <summary>
/// A numbering resolution as the tax authority issued it.
/// </summary>
/// <param name="DocumentType">The type of document it numbers.</param>
/// <param name="ResolutionNumber">The number of the resolution.</param>
/// <param name="TechnicalKey">The technical key, which goes into the CUFE.</param>
/// <param name="Prefix">The prefix authorized, letters or digits; it may be empty.</param>
/// <param name="From">The first number authorized.</param>
/// <param name="To">The last number authorized.</param>
/// <param name="ValidFrom">The first day of the validity.</param>
/// <param name="ValidUntil">The last day of the validity.</param>
public sealed record NumberingResolution(ElectronicDocumentType DocumentType, string ResolutionNumber, string? TechnicalKey, string Prefix, long From, long To, LocalDate ValidFrom, LocalDate ValidUntil);
