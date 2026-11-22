using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Simulated;

/// <summary>
/// The index that expires what the simulated provider received is created when the service starts, and a database that
/// refuses it never keeps the service, which also serves the real providers, from starting.
/// </summary>
public class SimulatedProviderDocumentIndexesTest
{
    [Fact]
    public async Task StartAsync_DatabaseAcceptsTheIndex_CreatesIt()
    {
        // Arrange
        var documents = new CountingDocuments(fails: false);

        // Act
        await new SimulatedProviderDocumentIndexes(documents, NullLogger<SimulatedProviderDocumentIndexes>.Instance).StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, documents.Calls);
    }

    [Fact]
    public async Task StartAsync_DatabaseRefusesTheIndex_DoesNotStopTheStart()
    {
        // Arrange
        var documents = new CountingDocuments(fails: true);

        // Act
        var exception = await Record.ExceptionAsync(() => new SimulatedProviderDocumentIndexes(documents, NullLogger<SimulatedProviderDocumentIndexes>.Instance).StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal((null, 1), (exception, documents.Calls));
    }

    [Fact]
    public void Initialize_RunTwice_CreatesTheIndexOfTheSimulatedDocumentsOnceAtTheStart()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        new Startup().Initialize(services, configuration);
        new Startup().Initialize(services, configuration);

        // Assert
        Assert.Single(services, x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SimulatedProviderDocumentIndexes));
    }

    [Fact]
    public void Retention_Default_IsThirtyDays()
    {
        // Assert
        Assert.Equal(TimeSpan.FromDays(30), new SimulatedOptions().Retention);
    }

    /// <summary>
    /// Documents that only count how many times their index was asked for, and may refuse it.
    /// </summary>
    private sealed class CountingDocuments(bool fails) : ISimulatedProviderDocumentRepository
    {
        public int Calls { get; private set; }

        public Task EnsureIndexesAsync(CancellationToken cancellationToken)
        {
            Calls++;

            return fails ? Task.FromException(new InvalidOperationException("The engine does not expire by an index.")) : Task.CompletedTask;
        }

        public Task<SimulatedProviderDocument> ReceiveAsync(SimulatedProviderDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SimulatedProviderDocument?> CheckAsync(SimulatedDocumentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SimulatedProviderDocument?> FindAsync(SimulatedDocumentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
