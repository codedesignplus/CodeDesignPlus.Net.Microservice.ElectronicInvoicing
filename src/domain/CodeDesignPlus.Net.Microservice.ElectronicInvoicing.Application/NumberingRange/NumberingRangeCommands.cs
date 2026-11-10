using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;

/// <summary>
/// Registers by hand a numbering resolution of the issuer of the tenant of the user.
/// </summary>
/// <param name="DocumentType">The type of document it numbers.</param>
/// <param name="ResolutionNumber">The number of the resolution.</param>
/// <param name="TechnicalKey">The technical key, which goes into the CUFE.</param>
/// <param name="Prefix">The prefix authorized.</param>
/// <param name="From">The first number authorized.</param>
/// <param name="To">The last number authorized.</param>
/// <param name="ValidFrom">The first day of the validity.</param>
/// <param name="ValidUntil">The last day of the validity.</param>
public record RegisterNumberingRangeCommand(ElectronicDocumentType DocumentType, string ResolutionNumber, string? TechnicalKey, string Prefix, long From, long To, LocalDate ValidFrom, LocalDate ValidUntil) : IRequest<Guid>
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="RegisterNumberingRangeCommand"/>.
/// </summary>
public class RegisterNumberingRangeValidator : AbstractValidator<RegisterNumberingRangeCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public RegisterNumberingRangeValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.ResolutionNumber).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.TechnicalKey).MaximumLength(FieldLength.Title);
        RuleFor(x => x.Prefix).NotNull();
        RuleFor(x => x.From).GreaterThanOrEqualTo(0);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x.ValidUntil).GreaterThanOrEqualTo(x => x.ValidFrom);
    }
}

/// <summary>
/// Handles <see cref="RegisterNumberingRangeCommand"/>.
/// </summary>
/// <remarks>
/// The id derives from the issuer, the type, the prefix and the resolution: registering it again ends well and keeps its
/// next number. When the provider of the issuer numbers, the range is kept as an informative mirror.
/// </remarks>
/// <param name="ranges">The repository of the ranges.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="user">The context of the user.</param>
public class RegisterNumberingRangeCommandHandler(INumberingRangeRepository ranges, IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IUserContext user) : IRequestHandler<RegisterNumberingRangeCommand, Guid>
{
    /// <summary>
    /// Registers the range unless it already exists.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the range.</returns>
    public async Task<Guid> Handle(RegisterNumberingRangeCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        var id = NumberingRangeAggregate.IdFor(issuer.Id, request.DocumentType, request.Prefix, request.ResolutionNumber);

        if (await ranges.ExistsAsync<NumberingRangeAggregate>(id, cancellationToken))
        {
            return id;
        }

        var source = factory.Get(issuer.Provider).Capabilities.Numbering == NumberingMode.ByPlatform ? NumberingSource.Platform : NumberingSource.Provider;
        var data = new NumberingResolution(request.DocumentType, request.ResolutionNumber, request.TechnicalKey, request.Prefix, request.From, request.To, request.ValidFrom, request.ValidUntil);

        await ranges.CreateIfAbsentAsync(NumberingRangeAggregate.Create(tenant, issuer.Id, data, source, user.IdUser), cancellationToken);

        return id;
    }
}

/// <summary>
/// Brings from the provider the numbering resolution of a prefix of the issuer of the tenant of the user, for a type of
/// document (the provider does not say which type a resolution numbers).
/// </summary>
/// <param name="DocumentType">The type of document the resolution numbers.</param>
/// <param name="Prefix">The prefix of the resolution in the provider.</param>
public record SyncNumberingRangeCommand(ElectronicDocumentType DocumentType, string Prefix) : IRequest<Guid>
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="SyncNumberingRangeCommand"/>.
/// </summary>
public class SyncNumberingRangeValidator : AbstractValidator<SyncNumberingRangeCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public SyncNumberingRangeValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.Prefix).NotNull();
    }
}

/// <summary>
/// Handles <see cref="SyncNumberingRangeCommand"/>.
/// </summary>
/// <param name="ranges">The repository of the ranges.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="resolver">Prepares the issuer for the adapter.</param>
/// <param name="user">The context of the user.</param>
public class SyncNumberingRangeCommandHandler(INumberingRangeRepository ranges, IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IProviderIssuerResolver resolver, IUserContext user) : IRequestHandler<SyncNumberingRangeCommand, Guid>
{
    /// <summary>
    /// Creates the range or refreshes it with what the provider says, keeping its next number.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the range.</returns>
    public async Task<Guid> Handle(SyncNumberingRangeCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        var provider = factory.FindProvisioning(issuer.Provider);

        ApplicationGuard.IsNull(provider, Errors.ProviderCannotProvisionIssuers);

        var resolutions = await provider.GetNumberingRangesAsync(await resolver.ResolveAsync(issuer, cancellationToken), cancellationToken);
        var found = resolutions.FirstOrDefault(x => string.Equals(x.Prefix, request.Prefix, StringComparison.Ordinal));

        ApplicationGuard.IsNull(found, Errors.ProviderResolutionNotFound);

        var data = new NumberingResolution(request.DocumentType, found.ResolutionNumber, found.TechnicalKey, found.Prefix, found.From, found.To, found.ValidFrom, found.ValidUntil);
        var id = NumberingRangeAggregate.IdFor(issuer.Id, request.DocumentType, found.Prefix, found.ResolutionNumber);
        var source = provider.Capabilities.Numbering == NumberingMode.ByPlatform ? NumberingSource.Platform : NumberingSource.Provider;

        await ranges.UpdateWithRetryAsync(
            async () => await ranges.FindAsync<NumberingRangeAggregate>(id, cancellationToken) ?? NumberingRangeAggregate.Create(tenant, issuer.Id, data, source, user.IdUser),
            range => range.Version == 0 || range.Refresh(data, user.IdUser),
            cancellationToken);

        return id;
    }
}

/// <summary>
/// The numbering ranges of the issuer of the tenant of the user.
/// </summary>
/// <param name="DocumentType">Only the ranges of this type; null for all.</param>
public record GetNumberingRangesQuery(ElectronicDocumentType? DocumentType) : IRequest<List<NumberingRangeDto>>
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Handles <see cref="GetNumberingRangesQuery"/>.
/// </summary>
/// <param name="ranges">The repository of the ranges.</param>
/// <param name="user">The context of the user.</param>
public class GetNumberingRangesQueryHandler(INumberingRangeRepository ranges, IUserContext user) : IRequestHandler<GetNumberingRangesQuery, List<NumberingRangeDto>>
{
    /// <summary>
    /// Reads the ranges.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranges, the earliest validity first.</returns>
    public async Task<List<NumberingRangeDto>> Handle(GetNumberingRangesQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);
        var types = request.DocumentType is { } type ? [type] : Enum.GetValues<ElectronicDocumentType>();
        var result = new List<NumberingRangeDto>();

        foreach (var each in types)
        {
            result.AddRange((await ranges.FindByTypeAsync(tenant, each, cancellationToken)).Select(NumberingRangeDto.Of));
        }

        return [.. result.OrderBy(x => x.DocumentType).ThenBy(x => x.ValidFrom)];
    }
}

/// <summary>
/// What the service shows of a numbering range.
/// </summary>
public class NumberingRangeDto
{
    /// <summary>
    /// The id of the range.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The type of document it numbers.
    /// </summary>
    public ElectronicDocumentType DocumentType { get; set; }

    /// <summary>
    /// The number of the resolution.
    /// </summary>
    public string ResolutionNumber { get; set; } = null!;

    /// <summary>
    /// Whether it has its technical key.
    /// </summary>
    public bool HasTechnicalKey { get; set; }

    /// <summary>
    /// The prefix.
    /// </summary>
    public string Prefix { get; set; } = null!;

    /// <summary>
    /// The first number.
    /// </summary>
    public long From { get; set; }

    /// <summary>
    /// The last number.
    /// </summary>
    public long To { get; set; }

    /// <summary>
    /// The next number to give.
    /// </summary>
    public long Next { get; set; }

    /// <summary>
    /// How many numbers are left.
    /// </summary>
    public long Remaining { get; set; }

    /// <summary>
    /// The first day of the validity.
    /// </summary>
    public LocalDate ValidFrom { get; set; }

    /// <summary>
    /// The last day of the validity.
    /// </summary>
    public LocalDate ValidUntil { get; set; }

    /// <summary>
    /// Who gives the numbers.
    /// </summary>
    public NumberingSource Source { get; set; }

    /// <summary>
    /// The range as the service shows it.
    /// </summary>
    /// <param name="range">The range.</param>
    /// <returns>The DTO.</returns>
    public static NumberingRangeDto Of(NumberingRangeAggregate range) => new()
    {
        Id = range.Id,
        DocumentType = range.DocumentType,
        ResolutionNumber = range.ResolutionNumber,
        HasTechnicalKey = !string.IsNullOrWhiteSpace(range.TechnicalKey),
        Prefix = range.Prefix,
        From = range.From,
        To = range.To,
        Next = range.Next,
        Remaining = range.Remaining,
        ValidFrom = range.ValidFrom,
        ValidUntil = range.ValidUntil,
        Source = range.Source
    };
}

/// <summary>
/// The round of the daily watch: announces the ranges that run out of numbers or of validity.
/// </summary>
public record WatchNumberingRangesCommand : IRequest<int>;

/// <summary>
/// Handles <see cref="WatchNumberingRangesCommand"/>.
/// </summary>
/// <remarks>
/// Each range is looked at on the calendar of its issuer. A range already announced is not announced again until it is
/// refreshed. Every range is tried; the round throws at the end if any failed.
/// </remarks>
/// <param name="ranges">The repository of the ranges.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
/// <param name="logger">The logger.</param>
public class WatchNumberingRangesCommandHandler(INumberingRangeRepository ranges, IIssuerRepository issuers, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options, ILogger<WatchNumberingRangesCommandHandler> logger) : IRequestHandler<WatchNumberingRangesCommand, int>
{
    /// <summary>
    /// Runs the watch.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many alerts were raised.</returns>
    /// <exception cref="InvalidOperationException">Thrown at the end when any range failed.</exception>
    public async Task<int> Handle(WatchNumberingRangesCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var alerts = options.Value.NumberingAlerts;

        // A day of margin on the zone: every zone is within a day of UTC.
        var watched = await ranges.FindWatchedAsync(now.InUtc().Date.PlusDays(-1), cancellationToken);
        var zones = new Dictionary<Guid, DateTimeZone>();
        var raised = 0;
        var failed = 0;

        foreach (var range in watched)
        {
            try
            {
                if (!zones.TryGetValue(range.IssuerId, out var zone))
                {
                    var issuer = await issuers.FindAsync<IssuerAggregate>(range.IssuerId, cancellationToken);

                    zone = issuer is null ? DateTimeZone.Utc : DateTimeZoneProviders.Tzdb[issuer.TimeZoneId];
                    zones[range.IssuerId] = zone;
                }

                var today = now.InZone(zone).Date;
                NumberingRangeAggregate? saved = null;

                await ranges.UpdateWithRetryAsync(
                    () => ranges.FindAsync<NumberingRangeAggregate>(range.Id, cancellationToken),
                    fresh =>
                    {
                        saved = null;
                        fresh.GetAndClearEvents();

                        if (!fresh.CheckRunningOut(today, alerts.RemainingPercentage, alerts.DaysBeforeExpiry))
                        {
                            return false;
                        }

                        saved = fresh;

                        return true;
                    },
                    cancellationToken);

                var events = saved?.GetAndClearEvents() ?? [];

                if (events.Count > 0)
                {
                    raised += events.Count;

                    await pubsub.PublishAsync(events, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;

                logger.LogError(exception, "The watch of numbering range {RangeId} failed.", range.Id);
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"The watch of {failed} of {watched.Count} numbering ranges failed.");
        }

        return raised;
    }
}
