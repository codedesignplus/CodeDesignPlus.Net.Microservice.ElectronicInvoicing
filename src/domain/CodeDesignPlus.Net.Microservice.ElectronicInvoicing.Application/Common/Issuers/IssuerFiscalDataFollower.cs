using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;

/// <summary>
/// The tax regime and the fiscal responsibilities an issuer must have, by the history managed from outside it.
/// </summary>
/// <param name="TaxRegimeCode">Code of the tax regime.</param>
/// <param name="TaxResponsibilities">Codes of every responsibility of the issuer: its own and the managed ones in force.</param>
public sealed record FiscalDataTarget(string TaxRegimeCode, List<string> TaxResponsibilities)
{
    /// <summary>
    /// Whether an issuer already has them.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <returns>True when its regime and its responsibilities are these.</returns>
    public bool IsMetBy(IssuerAggregate issuer) => string.Equals(issuer.TaxRegimeCode, TaxRegimeCode, StringComparison.OrdinalIgnoreCase) && FiscalResponsibilities.AreSame(issuer.TaxResponsibilities, TaxResponsibilities);

    /// <summary>
    /// What an issuer must have today by the history of its tenant.
    /// </summary>
    /// <param name="profile">The history; null when none arrived.</param>
    /// <param name="own">The responsibilities the issuer has or asks for; the managed ones among them are replaced.</param>
    /// <param name="noneCode">The code that means «no responsibility» in the list of the country of the issuer.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The target, or null without history or without a version in force today.</returns>
    public static FiscalDataTarget? Of(ManagedFiscalProfileAggregate? profile, IEnumerable<string> own, string? noneCode, Instant now)
    {
        if (profile?.InForceAt(now) is not { } entry)
        {
            return null;
        }

        return new FiscalDataTarget(entry.TaxRegimeCode, profile.ResponsibilitiesWith(own, entry, noneCode));
    }
}

/// <summary>
/// The fiscal data an issuer keeps when it is registered or edited: what was asked for, with the regime and the managed
/// responsibilities of the history of its tenant when one is in force.
/// </summary>
public static class IssuerFiscalDataOf
{
    /// <summary>
    /// Composes the fiscal data to keep.
    /// </summary>
    /// <remarks>
    /// The responsibilities the history manages are never taken from the request: the ones in force are added, and while
    /// no version is in force yet the issuer has none of them. The platform's issuer has no history.
    /// </remarks>
    /// <param name="asked">The fiscal data as they were asked for.</param>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="profiles">The histories managed from outside.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The options of the service, with the lists of each country.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fiscal data to keep.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>228</c> when there is no regime: none was asked for and no version is in force.</exception>
    public static async Task<IssuerFiscalData> ComposeAsync(IssuerFiscalData asked, Guid tenant, IManagedFiscalProfileRepository profiles, IClock clock, ElectronicInvoicingOptions options, CancellationToken cancellationToken)
    {
        var noneCode = options.IssuerOptions.NoResponsibilityCodeOf(asked.Country);
        var profile = PlatformScope.Owns(tenant) ? null : await profiles.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(tenant), cancellationToken);
        var own = (asked.TaxResponsibilities ?? []).Where(x => profile is null || !profile.ManagedResponsibilities.Contains(x, StringComparer.OrdinalIgnoreCase));
        var target = FiscalDataTarget.Of(profile, own, noneCode, clock.GetCurrentInstant());

        var data = target is null
            ? asked with { TaxResponsibilities = FiscalResponsibilities.Normalize(own, noneCode) }
            : asked with { TaxRegimeCode = target.TaxRegimeCode, TaxResponsibilities = target.TaxResponsibilities };

        ApplicationGuard.IsNullOrEmpty(data.TaxRegimeCode, Errors.TaxRegimeIsRequired);

        return data;
    }
}

/// <summary>
/// How an attempt to align an issuer with the fiscal data it must follow ended.
/// </summary>
/// <param name="Applied">Whether the issuer took new data.</param>
/// <param name="Rejection">The refusal of the provider, when it refused them: it is recorded on the issuer.</param>
public sealed record FiscalDataSyncResult(bool Applied, CodeDesignPlusException? Rejection)
{
    /// <summary>
    /// Nothing to do: no history, no issuer, no version in force, or the issuer already had them.
    /// </summary>
    public static readonly FiscalDataSyncResult Nothing = new(false, null);
}

/// <summary>
/// Aligns the issuer of a tenant with the tax regime and the fiscal responsibilities managed from outside it.
/// </summary>
public interface IIssuerFiscalDataFollower
{
    /// <summary>
    /// Sends to the provider, and then keeps on the issuer, the regime and the responsibilities in force today by the
    /// history of its tenant. A refusal of the provider is recorded on the issuer, which stops issuing until it accepts.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="updatedBy">Who applies them: the system user or the user that asked for it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How it ended.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>301</c> when the provider does not answer and <c>302</c> when it rejects the credentials: nothing is recorded, and the round tries again.</exception>
    Task<FiscalDataSyncResult> SyncAsync(Guid tenant, Guid updatedBy, CancellationToken cancellationToken);
}

/// <summary>
/// Implements <see cref="IIssuerFiscalDataFollower"/>.
/// </summary>
/// <remarks>
/// The provider goes first: the issuer only keeps what its provider accepted, so its documents never carry a regime the
/// provider does not have. An issuer that does not exist in a provider yet, or whose provider does not keep issuers, just
/// takes them. The managed responsibilities of the issuer are replaced and the rest stay as the issuer has them.
/// </remarks>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="profiles">The histories managed from outside.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="availability">Says whether the issuer can issue, for whoever listens.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service, with the lists of each country.</param>
/// <param name="logger">The logger.</param>
public sealed class IssuerFiscalDataFollower(IIssuerRepository issuers, IManagedFiscalProfileRepository profiles, IElectronicInvoiceProviderFactory factory, IPubSub pubsub, IIssuingAvailabilityAnnouncer availability, IClock clock, IOptions<ElectronicInvoicingOptions> options, ILogger<IssuerFiscalDataFollower> logger) : IIssuerFiscalDataFollower
{
    /// <inheritdoc/>
    public async Task<FiscalDataSyncResult> SyncAsync(Guid tenant, Guid updatedBy, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(tenant), cancellationToken);
        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        if (profile is null || issuer is null)
        {
            return FiscalDataSyncResult.Nothing;
        }

        var noneCode = options.Value.IssuerOptions.NoResponsibilityCodeOf(issuer.Country);
        var target = FiscalDataTarget.Of(profile, issuer.TaxResponsibilities, noneCode, clock.GetCurrentInstant());

        if (target is null || (target.IsMetBy(issuer) && issuer.FiscalDataRejectionCode is null))
        {
            return FiscalDataSyncResult.Nothing;
        }

        try
        {
            await this.SendToProviderAsync(issuer, target, cancellationToken);
        }
        catch (CodeDesignPlusException exception) when (!TransientFailures.IsTransient(exception) && !TransientFailures.IsCredentialsRejection(exception))
        {
            logger.LogWarning("The provider {Provider} refused the fiscal data of the issuer of tenant {Tenant} ({Code}): it does not issue until it accepts them.", issuer.Provider, tenant, exception.Code);

            await IssuerWrites.UpdateAsync(issuers, pubsub, availability, tenant, x => x.RecordFiscalDataRejection(exception.Code, updatedBy), cancellationToken);

            return new FiscalDataSyncResult(false, exception);
        }

        await IssuerWrites.UpdateAsync(issuers, pubsub, availability, tenant, x => x.FollowFiscalData(target.TaxRegimeCode, target.TaxResponsibilities, updatedBy), cancellationToken);

        logger.LogInformation("The issuer of tenant {Tenant} follows the regime {Regime} and the responsibilities {Responsibilities}.", tenant, target.TaxRegimeCode, string.Join(',', target.TaxResponsibilities));

        return new FiscalDataSyncResult(true, null);
    }

    private async Task SendToProviderAsync(IssuerAggregate issuer, FiscalDataTarget target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer.ProviderIssuerId) || factory.FindProvisioning(issuer.Provider) is not { } provisioning)
        {
            return;
        }

        var sent = ProviderIssuer.From(issuer) with { TaxRegimeCode = target.TaxRegimeCode, TaxResponsibilities = target.TaxResponsibilities };

        await provisioning.UpdateIssuerAsync(sent, cancellationToken);
    }
}
