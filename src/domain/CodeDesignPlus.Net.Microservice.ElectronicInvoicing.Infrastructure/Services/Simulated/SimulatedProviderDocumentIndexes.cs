namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// Creates, when the service starts, the index that expires what the simulated provider received.
/// </summary>
/// <remarks>
/// Unlike the unique index of the numbers, this one only cleans up: without it the documents of the simulation stay
/// until someone deletes them, and nothing else changes. So a failure to create it (an engine compatible with Mongo that
/// does not expire by an index, or a database that does not answer yet) is logged and never stops the start of the
/// service, which also serves the real providers.
/// </remarks>
/// <param name="documents">Where the simulated provider keeps its documents.</param>
/// <param name="logger">The logger.</param>
public sealed class SimulatedProviderDocumentIndexes(ISimulatedProviderDocumentRepository documents, ILogger<SimulatedProviderDocumentIndexes> logger) : IHostedService
{
    /// <summary>
    /// Creates the index, or logs why it could not.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the index exists or the failure is logged.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await documents.EnsureIndexesAsync(cancellationToken);

            logger.LogInformation("The index that expires the documents of the simulated provider is in place.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The index that expires the documents of the simulated provider could not be created: they are kept until it exists.");
        }
    }

    /// <summary>
    /// Nothing to stop.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
