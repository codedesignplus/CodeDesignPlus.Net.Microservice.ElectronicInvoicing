using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using Microsoft.Extensions.DependencyInjection;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

/// <summary>
/// The SDK runs every startup once per <c>AddCore</c>, and an entrypoint calls it several times (Redis, Observability,
/// Logger, the bus): the startup of the infrastructure registers its services once however many times it runs.
/// </summary>
public class InfrastructureStartupTest
{
    private static ServiceProvider Initialized(int times)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Entrypoint(), "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // As in production, where the environment repeats the routing of the file.
                ["ElectronicInvoicing:Routing:CO:0"] = "Alanube",
                ["ElectronicInvoicing:Routing:CO:1"] = "Aliaddo"
            })
            .Build();

        var services = new ServiceCollection();

        for (var i = 0; i < times; i++)
        {
            new Startup().Initialize(services, configuration);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Initialize_RunSeveralTimes_BindsTheListsOfTheConfigurationOnce()
    {
        // Arrange
        using var provider = Initialized(4);

        // Act
        var options = provider.GetRequiredService<IOptions<ElectronicInvoicingOptions>>().Value;

        // Assert
        Assert.Equal(["Alanube", "Aliaddo"], options.ProvidersFor("CO"));
    }

    [Fact]
    public void Initialize_RunTwice_CreatesTheIndexesOfTheDocumentsOnceAtTheStart()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(Entrypoint(), "appsettings.json")).Build();

        // Act
        new Startup().Initialize(services, configuration);
        new Startup().Initialize(services, configuration);

        // Assert
        Assert.Single(services, x => x.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && x.ImplementationType == typeof(Repositories.ElectronicDocumentIndexes));
    }

    [Fact]
    public void Initialize_RunTwice_RegistersEachAdapterOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(Entrypoint(), "appsettings.json")).Build();

        // Act
        new Startup().Initialize(services, configuration);
        new Startup().Initialize(services, configuration);

        // Assert
        Assert.Equal(3, services.Count(x => x.ServiceType == typeof(IElectronicInvoiceProvider)));
    }

    private static string Entrypoint()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "entrypoints")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "entrypoints", "CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest");
    }
}
