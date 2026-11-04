namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure;

/// <summary>
/// Registers the services of the infrastructure layer.
/// </summary>
/// <remarks>
/// The repositories are registered by <c>AddMongo</c>, which scans this assembly. The technology providers and their
/// options arrive with the first phase of the service.
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
    }
}
