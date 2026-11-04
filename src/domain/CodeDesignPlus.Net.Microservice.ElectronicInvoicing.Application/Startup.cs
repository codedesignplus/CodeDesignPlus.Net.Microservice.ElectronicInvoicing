using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application;

/// <summary>
/// Registers the services of the application layer.
/// </summary>
public class Startup : IStartup
{
    /// <summary>
    /// Registers the services that the handlers share.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the service.</param>
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.AddMemoryCache();
        services.TryAddSingleton<IProviderIssuerResolver, ProviderIssuerResolver>();
        services.TryAddScoped<IQuotaGate, QuotaGate>();
        services.TryAddScoped<INumberingService, NumberingService>();
        services.TryAddScoped<IDocumentContentResolver, DocumentContentResolver>();
    }
}
