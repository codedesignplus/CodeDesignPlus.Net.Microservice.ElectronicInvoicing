using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

/// <summary>
/// Replaces the history of the tax regime and the fiscal responsibilities the issuer of a tenant must follow, as the
/// service that owns them sends it, and aligns the issuer with the version in force today.
/// </summary>
/// <param name="Tenant">The tenant of the issuer.</param>
/// <param name="TimeZoneId">IANA time zone the days of the history are read in.</param>
/// <param name="ManagedResponsibilities">Codes of the responsibilities the history decides.</param>
/// <param name="Entries">The versions, each with the day it rules from.</param>
public record SetManagedFiscalProfileCommand(Guid Tenant, string TimeZoneId, List<string> ManagedResponsibilities, List<ManagedFiscalProfileEntry> Entries) : IRequest;

/// <summary>
/// Validates <see cref="SetManagedFiscalProfileCommand"/>.
/// </summary>
public class SetManagedFiscalProfileValidator : AbstractValidator<SetManagedFiscalProfileCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public SetManagedFiscalProfileValidator()
    {
        RuleFor(x => x.Tenant).NotEmpty();
        RuleFor(x => x.TimeZoneId).NotEmpty();
        RuleFor(x => x.Entries).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="SetManagedFiscalProfileCommand"/>.
/// </summary>
/// <remarks>
/// The history is kept first, so a version that rules from a later day is applied by the round when the day comes, and an
/// issuer registered later is born with it. Aligning the issuer now is a courtesy to the sender: a provider that does not
/// answer, or rejects the credentials, does not fail the push, because the round tries again within the hour.
/// </remarks>
/// <param name="profiles">The histories managed from outside.</param>
/// <param name="follower">Aligns the issuer with the history.</param>
/// <param name="options">The options of the service: the system user signs what it writes.</param>
/// <param name="logger">The logger.</param>
public class SetManagedFiscalProfileCommandHandler(IManagedFiscalProfileRepository profiles, IIssuerFiscalDataFollower follower, IOptions<ElectronicInvoicingOptions> options, ILogger<SetManagedFiscalProfileCommandHandler> logger) : IRequestHandler<SetManagedFiscalProfileCommand>
{
    /// <summary>
    /// Keeps the history and aligns the issuer.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(SetManagedFiscalProfileCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);
        ApplicationGuard.IsTrue(PlatformScope.Owns(request.Tenant), Errors.TenantIsReserved);

        var systemUser = options.Value.SystemUser;
        var profile = await profiles.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(request.Tenant), cancellationToken);

        if (profile is null)
        {
            await profiles.UpsertAsync(ManagedFiscalProfileAggregate.Create(request.Tenant, request.TimeZoneId, request.ManagedResponsibilities ?? [], request.Entries, systemUser), cancellationToken);
        }
        else if (profile.Replace(request.TimeZoneId, request.ManagedResponsibilities ?? [], request.Entries, systemUser))
        {
            await profiles.UpdateAsync(profile, cancellationToken);
        }

        try
        {
            await follower.SyncAsync(request.Tenant, systemUser, cancellationToken);
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsTransient(exception) || TransientFailures.IsCredentialsRejection(exception))
        {
            logger.LogWarning("The issuer of tenant {Tenant} was not aligned with its fiscal data now ({Code}); the round tries again.", request.Tenant, exception.Code);
        }
    }
}

/// <summary>
/// Aligns the issuer of the tenant of the user with the tax regime and the fiscal responsibilities it must follow, now.
/// </summary>
public record SyncIssuerFiscalProfileCommand : IRequest;

/// <summary>
/// Handles <see cref="SyncIssuerFiscalProfileCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="profiles">The histories managed from outside.</param>
/// <param name="follower">Aligns the issuer with the history.</param>
/// <param name="clock">The clock.</param>
/// <param name="user">The context of the user.</param>
public class SyncIssuerFiscalProfileCommandHandler(IIssuerRepository issuers, IManagedFiscalProfileRepository profiles, IIssuerFiscalDataFollower follower, IClock clock, IUserContext user) : IRequestHandler<SyncIssuerFiscalProfileCommand>
{
    /// <summary>
    /// Aligns the issuer.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>202</c> without issuer, <c>229</c> when no version is in force for the tenant, or the refusal of the provider (for example <c>318</c>), which stays recorded on the issuer.</exception>
    public async Task Handle(SyncIssuerFiscalProfileCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(false, user);

        ApplicationGuard.IsFalse(await issuers.ExistsAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken), Errors.IssuerNotFound);

        var profile = await profiles.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsTrue(profile?.InForceAt(clock.GetCurrentInstant()) is null, Errors.ManagedFiscalProfileNotReceived);

        var result = await follower.SyncAsync(tenant, user.IdUser, cancellationToken);

        if (result.Rejection is not null)
        {
            throw result.Rejection;
        }
    }
}

/// <summary>
/// Aligns every issuer with the version of its history in force today: the versions whose day came, and the issuers whose
/// provider refused or did not answer before.
/// </summary>
public record ApplyManagedFiscalProfilesCommand : IRequest<int>;

/// <summary>
/// Handles <see cref="ApplyManagedFiscalProfilesCommand"/>.
/// </summary>
/// <remarks>
/// Every tenant is tried. What happened to each issuer stays on it (the data it took, or the refusal of its provider), and
/// the round throws at the end when any failed or was refused, so the panel of the jobs shows it (rule 23).
/// </remarks>
/// <param name="profiles">The histories managed from outside.</param>
/// <param name="follower">Aligns the issuer with the history.</param>
/// <param name="options">The options of the service: the system user signs what it writes.</param>
/// <param name="logger">The logger.</param>
public class ApplyManagedFiscalProfilesCommandHandler(IManagedFiscalProfileRepository profiles, IIssuerFiscalDataFollower follower, IOptions<ElectronicInvoicingOptions> options, ILogger<ApplyManagedFiscalProfilesCommandHandler> logger) : IRequestHandler<ApplyManagedFiscalProfilesCommand, int>
{
    /// <summary>
    /// Runs the round.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many issuers took new data.</returns>
    /// <exception cref="InvalidOperationException">Thrown at the end when any issuer failed or its provider refused the data.</exception>
    public async Task<int> Handle(ApplyManagedFiscalProfilesCommand request, CancellationToken cancellationToken)
    {
        var tenants = await profiles.FindTenantsAsync(cancellationToken);
        var applied = 0;
        var failed = 0;

        foreach (var tenant in tenants)
        {
            try
            {
                var result = await follower.SyncAsync(tenant, options.Value.SystemUser, cancellationToken);

                if (result.Applied)
                {
                    applied++;
                }

                if (result.Rejection is not null)
                {
                    failed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;

                logger.LogError(exception, "The issuer of tenant {Tenant} was not aligned with its fiscal data.", tenant);
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"{failed} of {tenants.Count} issuers were not aligned with their fiscal data.");
        }

        return applied;
    }
}
