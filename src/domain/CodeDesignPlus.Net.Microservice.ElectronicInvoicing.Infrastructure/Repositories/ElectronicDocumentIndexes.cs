namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Repositories;

/// <summary>
/// Creates the indexes of the electronic documents when the service starts, in every entrypoint.
/// </summary>
/// <remarks>
/// Creating an index that already exists is a no-op in Mongo, so the three entrypoints can run it at once. A failure stops
/// the start on purpose: without the unique index, two requests with the same number at once could both be saved.
/// </remarks>
/// <param name="scopes">Creates the scope the repository is resolved in.</param>
/// <param name="logger">The logger.</param>
public sealed class ElectronicDocumentIndexes(IServiceScopeFactory scopes, ILogger<ElectronicDocumentIndexes> logger) : IHostedService
{
    /// <summary>
    /// Creates the indexes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the indexes exist.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IElectronicDocumentRepository>().EnsureIndexesAsync(cancellationToken);

        logger.LogInformation("The indexes of the electronic documents are in place.");
    }

    /// <summary>
    /// Nothing to stop.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
