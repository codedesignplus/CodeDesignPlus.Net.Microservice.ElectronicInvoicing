using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application;

/// <summary>
/// Registers the services of the application layer.
/// </summary>
public class Startup : IStartup
{
    /// <summary>
    /// Configures the mappings of the application layer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the service.</param>
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        MapsterConfigElectronicInvoicing.Configure();
    }
}
