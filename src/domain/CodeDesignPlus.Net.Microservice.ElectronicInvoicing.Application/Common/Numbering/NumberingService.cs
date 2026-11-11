using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;

/// <summary>
/// Chooses the numbering range of a document and gives its number.
/// </summary>
public interface INumberingService
{
    /// <summary>
    /// The range that numbers a type of document on a day, without taking a number.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The range.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>103</c>, <c>104</c> or <c>207</c> when no range can number.</exception>
    Task<NumberingRangeAggregate> SelectAsync(Guid tenant, ElectronicDocumentType type, LocalDate today, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the next number of a range; two documents at once never get the same one.
    /// </summary>
    /// <param name="rangeId">The id of the range.</param>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number.</returns>
    Task<DocumentNumber> ReserveAsync(Guid rangeId, LocalDate today, CancellationToken cancellationToken);

    /// <summary>
    /// The resolution of a range as the adapters send it.
    /// </summary>
    /// <param name="rangeId">The id of the range.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolution, or null when the range no longer exists.</returns>
    Task<ProviderNumberingRange?> DescribeAsync(Guid rangeId, CancellationToken cancellationToken);

    /// <summary>
    /// The range that authorizes a number given by the caller, without taking anything from it.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="type">The type of document.</param>
    /// <param name="number">The number the caller gives.</param>
    /// <param name="today">The day, in the time zone of the issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The range; null when the issuer has no range for the type, so the number goes without a resolution.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>221</c> when no range of the type covers the number, or <c>222</c> when the one that covers it is not valid today.</exception>
    Task<NumberingRangeAggregate?> MatchAsync(Guid tenant, ElectronicDocumentType type, DocumentNumber number, LocalDate today, CancellationToken cancellationToken);
}

/// <summary>
/// Chooses the numbering range of a document and gives its number, with optimistic concurrency on the range.
/// </summary>
/// <param name="repository">The repository of the ranges.</param>
public sealed class NumberingService(INumberingRangeRepository repository) : INumberingService
{
    /// <inheritdoc/>
    public async Task<NumberingRangeAggregate> SelectAsync(Guid tenant, ElectronicDocumentType type, LocalDate today, CancellationToken cancellationToken)
    {
        var ranges = (await repository.FindByTypeAsync(tenant, type, cancellationToken)).Where(x => x.Source == NumberingSource.Platform).ToList();

        var usable = ranges.Where(x => x.IsUsableOn(today)).OrderBy(x => x.ValidFrom).ThenBy(x => x.From).FirstOrDefault();

        if (usable is not null)
        {
            return usable;
        }

        // Say why, so whoever fixes it knows whether to ask for more numbers or for a new validity.
        DomainGuard.IsTrue(ranges.Any(x => today >= x.ValidFrom && today <= x.ValidUntil && x.Remaining == 0), DomainErrors.NumberingRangeExhausted);
        DomainGuard.IsTrue(ranges.Count > 0 && ranges.All(x => today > x.ValidUntil), DomainErrors.NumberingRangeExpired);

        throw ApplicationErrors.NoUsableNumberingRange();
    }

    /// <inheritdoc/>
    public async Task<DocumentNumber> ReserveAsync(Guid rangeId, LocalDate today, CancellationToken cancellationToken)
    {
        DocumentNumber? number = null;

        await repository.UpdateWithRetryAsync(
            () => repository.FindAsync<NumberingRangeAggregate>(rangeId, cancellationToken),
            range =>
            {
                number = range.Reserve(today);

                return true;
            },
            cancellationToken);

        return number ?? throw ApplicationErrors.NoUsableNumberingRange();
    }

    /// <inheritdoc/>
    public async Task<ProviderNumberingRange?> DescribeAsync(Guid rangeId, CancellationToken cancellationToken)
    {
        var range = await repository.FindAsync<NumberingRangeAggregate>(rangeId, cancellationToken);

        return range is null ? null : new ProviderNumberingRange(range.TechnicalKey, range.Prefix, range.ResolutionNumber, range.From, range.To, range.ValidFrom, range.ValidUntil);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Whether a type needs a resolution is not decided here: some tax authorities give none to the notes. An issuer with
    /// ranges for the type must number inside one of them; an issuer without any sends the number as it comes, and the
    /// adapter of its provider refuses it if the provider asks for a resolution.
    /// </remarks>
    public async Task<NumberingRangeAggregate?> MatchAsync(Guid tenant, ElectronicDocumentType type, DocumentNumber number, LocalDate today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(number);

        var ranges = await repository.FindByTypeAsync(tenant, type, cancellationToken);

        if (ranges.Count == 0)
        {
            return null;
        }

        var covering = ranges.Where(x => x.Covers(number)).ToList();

        ApplicationGuard.IsTrue(covering.Count == 0, Errors.CallerNumberOutOfRange);

        var valid = covering.Where(x => x.IsValidOn(today)).OrderBy(x => x.ValidFrom).FirstOrDefault();

        ApplicationGuard.IsNull(valid, Errors.CallerNumberRangeNotValid);

        return valid;
    }
}
