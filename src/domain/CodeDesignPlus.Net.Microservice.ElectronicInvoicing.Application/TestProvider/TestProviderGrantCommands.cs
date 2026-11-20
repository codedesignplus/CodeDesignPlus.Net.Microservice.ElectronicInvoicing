using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.TestProvider;

/// <summary>
/// Lets a tenant issue with a test provider: an adapter that never reaches a tax authority.
/// </summary>
/// <param name="Tenant">The tenant that is allowed.</param>
public record GrantTestProviderCommand(Guid Tenant) : IRequest;

/// <summary>
/// Validates <see cref="GrantTestProviderCommand"/>.
/// </summary>
public class GrantTestProviderValidator : AbstractValidator<GrantTestProviderCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public GrantTestProviderValidator()
    {
        RuleFor(x => x.Tenant).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="GrantTestProviderCommand"/>.
/// </summary>
/// <remarks>
/// Granting what is already granted ends well without writing nor announcing anything. The tenant reserved for the
/// platform's issuer is refused (<c>154</c>).
/// </remarks>
/// <param name="grants">The repository of the permissions.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="user">The context of the user, who signs the permission.</param>
/// <param name="clock">The clock.</param>
public class GrantTestProviderCommandHandler(ITestProviderGrantRepository grants, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, IUserContext user, IClock clock) : IRequestHandler<GrantTestProviderCommand>
{
    /// <summary>
    /// Grants the permission and announces it.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(GrantTestProviderCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var now = clock.GetCurrentInstant();
        TestProviderGrantAggregate? saved = null;

        await grants.UpdateWithRetryAsync(
            async () => await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(request.Tenant), cancellationToken) ?? TestProviderGrantAggregate.Grant(request.Tenant, user.IdUser, now),
            grant =>
            {
                saved = null;

                // A permission that was never saved (version 0) comes already granted, with its event.
                if (grant.Version > 0)
                {
                    grant.GetAndClearEvents();

                    if (!grant.GrantAgain(user.IdUser, now))
                    {
                        return false;
                    }
                }

                saved = grant;

                return true;
            },
            cancellationToken);

        await TestProviderGrantWrites.PublishAsync(saved, pubsub, availability, cancellationToken);
    }
}

/// <summary>
/// Takes from a tenant the permission to issue with a test provider: its issuer stops issuing with one.
/// </summary>
/// <param name="Tenant">The tenant that loses it.</param>
public record RevokeTestProviderCommand(Guid Tenant) : IRequest;

/// <summary>
/// Validates <see cref="RevokeTestProviderCommand"/>.
/// </summary>
public class RevokeTestProviderValidator : AbstractValidator<RevokeTestProviderCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public RevokeTestProviderValidator()
    {
        RuleFor(x => x.Tenant).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="RevokeTestProviderCommand"/>.
/// </summary>
/// <remarks>
/// Revoking what was never granted, or is already revoked, ends well without writing nor announcing anything. Nothing
/// already issued is touched: the issuer of the tenant only stops receiving new documents (<c>225</c>).
/// </remarks>
/// <param name="grants">The repository of the permissions.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="user">The context of the user, who signs the revocation.</param>
/// <param name="clock">The clock.</param>
public class RevokeTestProviderCommandHandler(ITestProviderGrantRepository grants, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, IUserContext user, IClock clock) : IRequestHandler<RevokeTestProviderCommand>
{
    /// <summary>
    /// Revokes the permission and announces it.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(RevokeTestProviderCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var id = TestProviderGrantAggregate.IdFor(request.Tenant);

        if (!await grants.ExistsAsync<TestProviderGrantAggregate>(id, cancellationToken))
        {
            return;
        }

        var now = clock.GetCurrentInstant();
        TestProviderGrantAggregate? saved = null;

        await grants.UpdateWithRetryAsync(
            () => grants.FindAsync<TestProviderGrantAggregate>(id, cancellationToken),
            grant =>
            {
                saved = null;
                grant.GetAndClearEvents();

                if (!grant.Revoke(user.IdUser, now))
                {
                    return false;
                }

                saved = grant;

                return true;
            },
            cancellationToken);

        await TestProviderGrantWrites.PublishAsync(saved, pubsub, availability, cancellationToken);
    }
}

/// <summary>
/// The permission of a tenant to issue with a test provider, as it stands now.
/// </summary>
/// <param name="Tenant">The tenant.</param>
public record GetTestProviderGrantQuery(Guid Tenant) : IRequest<TestProviderGrantDto>;

/// <summary>
/// Handles <see cref="GetTestProviderGrantQuery"/>.
/// </summary>
/// <param name="grants">The repository of the permissions.</param>
/// <param name="options">The options of the service, which list the test providers.</param>
public class GetTestProviderGrantQueryHandler(ITestProviderGrantRepository grants, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<GetTestProviderGrantQuery, TestProviderGrantDto>
{
    /// <summary>
    /// Reads the permission.
    /// </summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The permission; not granted when the tenant never had one.</returns>
    public async Task<TestProviderGrantDto> Handle(GetTestProviderGrantQuery request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);
        DomainGuard.GuidIsEmpty(request.Tenant, DomainErrors.TenantIsRequired);

        var grant = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(request.Tenant), cancellationToken);

        return TestProviderGrantDto.From(request.Tenant, grant, options.Value.TestProviders);
    }
}

/// <summary>
/// What the service shows of the permission of a tenant to issue with a test provider.
/// </summary>
public class TestProviderGrantDto : IDtoBase
{
    /// <summary>
    /// The id of the permission, derived from the tenant.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// The tenant.
    /// </summary>
    public Guid Tenant { get; set; }

    /// <summary>
    /// Whether the tenant may issue with a test provider now.
    /// </summary>
    public bool Granted { get; set; }

    /// <summary>
    /// Who granted it last; null when it never was.
    /// </summary>
    public Guid? GrantedBy { get; set; }

    /// <summary>
    /// When it was granted last; null when it never was.
    /// </summary>
    public Instant? GrantedAt { get; set; }

    /// <summary>
    /// Who revoked it, while it is revoked.
    /// </summary>
    public Guid? RevokedBy { get; set; }

    /// <summary>
    /// When it was revoked, while it is revoked.
    /// </summary>
    public Instant? RevokedAt { get; set; }

    /// <summary>
    /// The codes of the test providers of this deployment: what the permission lets the issuer of the tenant choose.
    /// </summary>
    public List<string> TestProviders { get; set; } = [];

    /// <summary>
    /// The permission as the service shows it.
    /// </summary>
    /// <param name="tenant">The tenant asked about.</param>
    /// <param name="grant">Its permission, if it ever had one.</param>
    /// <param name="testProviders">The test providers of the deployment.</param>
    /// <returns>The DTO; not granted without a permission.</returns>
    public static TestProviderGrantDto From(Guid tenant, TestProviderGrantAggregate? grant, IEnumerable<string> testProviders) => new()
    {
        Id = TestProviderGrantAggregate.IdFor(tenant),
        Tenant = tenant,
        Granted = grant?.Granted ?? false,
        GrantedBy = grant?.GrantedBy,
        GrantedAt = grant?.GrantedAt,
        RevokedBy = grant?.RevokedBy,
        RevokedAt = grant?.RevokedAt,
        TestProviders = [.. testProviders]
    };
}

/// <summary>
/// What the commands of the permission share.
/// </summary>
public static class TestProviderGrantWrites
{
    /// <summary>
    /// Publishes the events of a permission after it was saved and, when the issuer of the tenant issues with a test
    /// provider that needs the permission, announces how it stands: granting or revoking changes whether it can issue.
    /// </summary>
    /// <param name="saved">The saved permission; null when nothing was written.</param>
    /// <param name="pubsub">The bus.</param>
    /// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that ends when the events are published.</returns>
    public static async Task PublishAsync(TestProviderGrantAggregate? saved, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, CancellationToken cancellationToken)
    {
        var events = saved?.GetAndClearEvents() ?? [];

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }

        if (saved is not null)
        {
            await availability.AnnounceForTestProviderAsync(saved.Tenant, cancellationToken);
        }
    }
}
