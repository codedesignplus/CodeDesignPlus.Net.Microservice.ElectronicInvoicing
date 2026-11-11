using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Repositories;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Files;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Vault;
using CodeDesignPlus.Net.Resilience.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure;

/// <summary>
/// Registers the services of the infrastructure layer: the options, the adapters of the providers and their factory,
/// the cipher of the Vault and the store of the files.
/// </summary>
/// <remarks>
/// The repositories are registered by <c>AddMongo</c>, which scans this assembly. Every adapter is registered; which one
/// an issuer uses is chosen by <c>ElectronicInvoicing:Routing</c>, so adding a provider is registering its adapter here.
/// </remarks>
public class Startup : IStartup
{
    /// <summary>
    /// Registers the services of the infrastructure layer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the service.</param>
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        // The SDK runs every startup once per AddCore, and the entrypoints reach AddCore several times (Redis,
        // Observability, Logger, the bus). Running again would bind each section again over the same options, and the
        // binder adds to the lists it finds (Routing, AlertPercentages…), and would register each adapter again.
        if (services.Any(x => x.ServiceType == typeof(Initialized)))
        {
            return;
        }

        services.AddSingleton<Initialized>();

        services
            .AddOptions<ElectronicInvoicingOptions>()
            .Bind(configuration.GetSection(ElectronicInvoicingOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var aliaddoSection = configuration.GetSection(AliaddoOptions.Section);
        var aliaddo = aliaddoSection.Get<AliaddoOptions>() ?? new AliaddoOptions();

        services
            .AddOptions<AliaddoOptions>()
            .Bind(aliaddoSection)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var alanubeSection = configuration.GetSection(AlanubeOptions.Section);
        var alanube = alanubeSection.Get<AlanubeOptions>() ?? new AlanubeOptions();

        services
            .AddOptions<AlanubeOptions>()
            .Bind(alanubeSection)
            // The token may live under another key (an environment variable on a developer's machine): it is read from
            // there when the Vault did not give it.
            .PostConfigure(x => x.Master.Token = string.IsNullOrWhiteSpace(x.Master.Token) && !string.IsNullOrWhiteSpace(x.Master.TokenKey) ? configuration[x.Master.TokenKey] : x.Master.Token)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<SimulatedOptions>()
            .Bind(configuration.GetSection(SimulatedOptions.Section));

        services.AddResilience();

        services
            .AddHttpClient(AliaddoClient.ReadsClientName, client => client.BaseAddress = aliaddo.BaseUrl)
            .AddResiliencePolicies(aliaddo.Resilience);

        // The sends never retry by themselves: a send that timed out may have arrived (see AliaddoClient).
        services
            .AddHttpClient(AliaddoClient.WritesClientName, client => client.BaseAddress = aliaddo.BaseUrl)
            .AddResilienceHandler($"{AliaddoClient.WritesClientName}-transport", builder => builder
                .AddTimeout(aliaddo.SendTimeout)
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions()));

        services
            .AddHttpClient(AliaddoClient.FilesClientName, client => client.BaseAddress = aliaddo.BaseUrl)
            .AddResiliencePolicies(aliaddo.Resilience);

        // The base of Alanube carries its version (…/col/v1): it ends in a slash so the relative paths go under it.
        var alanubeBase = alanube.BaseUrl is null ? null : new Uri(alanube.BaseUrl.ToString().TrimEnd('/') + "/");

        services
            .AddHttpClient(AlanubeClient.ReadsClientName, client => client.BaseAddress = alanubeBase)
            .AddResiliencePolicies(alanube.Resilience);

        // The sends never retry by themselves: the next round sends again with the same idempotency key (see AlanubeClient).
        services
            .AddHttpClient(AlanubeClient.WritesClientName, client => client.BaseAddress = alanubeBase)
            .AddResilienceHandler($"{AlanubeClient.WritesClientName}-transport", builder => builder
                .AddTimeout(alanube.SendTimeout)
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions()));

        // The signed links of the files redirect to the storage; they go with no token.
        services
            .AddHttpClient(AlanubeClient.FilesClientName)
            .AddResiliencePolicies(alanube.Resilience);

        services.AddSingleton<IAliaddoClient, AliaddoClient>();
        services.AddSingleton<IElectronicInvoiceProvider, AliaddoProvider>();
        services.AddSingleton<IAlanubeClient, AlanubeClient>();
        services.AddSingleton<IElectronicInvoiceProvider, AlanubeProvider>();
        services.AddSingleton<IElectronicInvoiceProvider, SimulatedProvider>();
        services.AddSingleton<IElectronicInvoiceProviderFactory, ElectronicInvoiceProviderFactory>();

        services.AddSingleton<IIssuerSecretCipher, VaultIssuerSecretCipher>();
        services.AddScoped<IDocumentFileStore, FileStorageDocumentFileStore>();

        services.AddHostedService<ElectronicDocumentIndexes>();
    }

    /// <summary>
    /// Marks the service collection where this startup already registered its services.
    /// </summary>
    private sealed class Initialized;
}
