using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

/// <summary>
/// Registers the webhooks of this service in every issuer already created in a provider that does not have them as the
/// configuration has them now.
/// </summary>
public record SyncProviderWebhooksCommand : IRequest<int>;

/// <summary>
/// Handles <see cref="SyncProviderWebhooksCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// The webhooks travel when the issuer is created in the provider, so an issuer created before they were turned on, or
/// before their address or secret changed, never calls. Each issuer keeps the fingerprint of the webhooks registered for
/// it (<see cref="IssuerAggregate.WebhooksFingerprint"/>): the round only looks at the ones whose fingerprint is not the
/// current one, so with nothing to do it is one query. A provider with the webhooks off has no fingerprint and is skipped.
/// </para>
/// <para>
/// Registering them again changes nothing in the provider, so a round that saved nothing after registering them repeats it
/// harmlessly. Every issuer is tried; the round throws at the end if any failed, so the panel of the jobs shows it.
/// </para>
/// </remarks>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="providers">The adapters of the providers.</param>
/// <param name="options">The options of the service: the system user signs the change.</param>
/// <param name="logger">The logger.</param>
public class SyncProviderWebhooksCommandHandler(IIssuerRepository issuers, IEnumerable<IElectronicInvoiceProvider> providers, IOptions<ElectronicInvoicingOptions> options, ILogger<SyncProviderWebhooksCommandHandler> logger) : IRequestHandler<SyncProviderWebhooksCommand, int>
{
    /// <summary>
    /// Runs the round.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many issuers got their webhooks.</returns>
    /// <exception cref="InvalidOperationException">Thrown at the end when any issuer failed.</exception>
    public async Task<int> Handle(SyncProviderWebhooksCommand request, CancellationToken cancellationToken)
    {
        var registered = 0;
        var failed = 0;
        var looked = 0;

        foreach (var registrar in providers.OfType<IProviderWebhookRegistrar>())
        {
            if (registrar.WebhooksFingerprint is not { } fingerprint)
            {
                continue;
            }

            var pending = await issuers.FindIdsAwaitingWebhooksAsync(registrar.Code, fingerprint, cancellationToken);

            looked += pending.Count;

            foreach (var id in pending)
            {
                try
                {
                    if (await this.RegisterAsync(registrar, id, fingerprint, cancellationToken))
                    {
                        registered++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;

                    logger.LogError(exception, "The webhooks of {Provider} were not registered for issuer {IssuerId}.", registrar.Code, id);
                }
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"The webhooks of {failed} of {looked} issuers were not registered.");
        }

        return registered;
    }

    private async Task<bool> RegisterAsync(IProviderWebhookRegistrar registrar, Guid id, string fingerprint, CancellationToken cancellationToken)
    {
        var issuer = await issuers.FindAsync<IssuerAggregate>(id, cancellationToken);

        // It changed provider or left it between the query and now: the next round sees it as it is.
        if (issuer is null || !string.Equals(issuer.Provider, registrar.Code, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(issuer.ProviderIssuerId))
        {
            return false;
        }

        await registrar.RegisterWebhooksAsync(ProviderIssuer.From(issuer), cancellationToken);

        var saved = false;

        await issuers.UpdateWithRetryAsync(
            () => issuers.FindAsync<IssuerAggregate>(id, cancellationToken),
            fresh =>
            {
                saved = string.Equals(fresh.ProviderIssuerId, issuer.ProviderIssuerId, StringComparison.Ordinal) && fresh.RecordWebhooks(fingerprint, options.Value.SystemUser);

                return saved;
            },
            cancellationToken);

        logger.LogInformation("The webhooks of {Provider} were registered for issuer {IssuerId} of tenant {Tenant}.", registrar.Code, id, issuer.Tenant);

        return saved;
    }
}
