using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services;

/// <summary>
/// Finds the adapter of a provider by its code among the registered ones. Adding a provider is registering its adapter;
/// nothing here changes.
/// </summary>
/// <param name="providers">Every registered adapter.</param>
public sealed class ElectronicInvoiceProviderFactory(IEnumerable<IElectronicInvoiceProvider> providers) : IElectronicInvoiceProviderFactory
{
    private readonly List<IElectronicInvoiceProvider> adapters = [.. providers];

    /// <inheritdoc/>
    public IElectronicInvoiceProvider Get(string provider) => Find(provider) ?? throw ApplicationErrors.Of(AppErrors.ProviderNotSupported);

    /// <inheritdoc/>
    public IElectronicInvoiceProvider? Find(string provider) => adapters.FirstOrDefault(x => string.Equals(x.Code, provider, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public IIssuerProvisioningProvider? FindProvisioning(string provider) => Find(provider) as IIssuerProvisioningProvider;

    /// <inheritdoc/>
    public IProviderSetupProvider? FindSetup(string provider) => Find(provider) as IProviderSetupProvider;

    /// <inheritdoc/>
    public IProviderEventReader? FindEvents(string provider) => Find(provider) as IProviderEventReader;
}
