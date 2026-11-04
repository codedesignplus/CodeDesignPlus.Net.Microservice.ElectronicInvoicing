using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer;

/// <summary>
/// What the commands of the issuer share: how a change is saved and which providers a country allows.
/// </summary>
public static class IssuerWrites
{
    /// <summary>
    /// Applies a change to the issuer of a tenant with optimistic concurrency, and publishes its events after saving.
    /// </summary>
    /// <param name="issuers">The repository of the issuers.</param>
    /// <param name="pubsub">The bus.</param>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="change">The change; it runs again on a fresh issuer if another process wrote it meanwhile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved issuer.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>202</c> when the tenant has no issuer.</exception>
    public static async Task<IssuerAggregate> UpdateAsync(IIssuerRepository issuers, IPubSub pubsub, Guid tenant, Action<IssuerAggregate> change, CancellationToken cancellationToken)
    {
        IssuerAggregate? saved = null;

        await issuers.UpdateWithRetryAsync(
            () => issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken),
            fresh =>
            {
                saved = null;
                fresh.GetAndClearEvents();

                change(fresh);

                saved = fresh;

                return true;
            },
            cancellationToken);

        ApplicationGuard.IsNull(saved, Errors.IssuerNotFound);

        var events = saved.GetAndClearEvents();

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }

        return saved;
    }

    /// <summary>
    /// The adapter of a provider an issuer of a country may choose.
    /// </summary>
    /// <param name="options">The options of the service.</param>
    /// <param name="factory">The adapters.</param>
    /// <param name="country">The country of the issuer.</param>
    /// <param name="provider">The provider chosen; null for the default of the country.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="Exceptions.CodeDesignPlusException">Thrown with <c>203</c> when the country does not allow it or no adapter serves it.</exception>
    public static IElectronicInvoiceProvider AllowedProvider(ElectronicInvoicingOptions options, IElectronicInvoiceProviderFactory factory, string country, string? provider)
    {
        var allowed = options.ProvidersFor(country);
        var chosen = string.IsNullOrWhiteSpace(provider) ? allowed.FirstOrDefault() : allowed.FirstOrDefault(x => string.Equals(x, provider, StringComparison.OrdinalIgnoreCase));

        ApplicationGuard.IsNullOrEmpty(chosen, Errors.ProviderNotSupported);

        var adapter = factory.Find(chosen!);

        ApplicationGuard.IsNull(adapter, Errors.ProviderNotSupported);

        return adapter;
    }
}
