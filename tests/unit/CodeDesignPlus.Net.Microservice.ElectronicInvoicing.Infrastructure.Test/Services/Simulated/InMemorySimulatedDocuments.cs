using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Simulated;

/// <summary>
/// The documents of the simulated provider kept in memory, for the tests of what the provider answers. It behaves as the
/// database does: it hands over copies, never the row it keeps, and each write is atomic. Sharing one between two
/// providers is two replicas over the same database.
/// </summary>
/// <remarks>
/// What Mongo really does (the serialization of each member, the upsert, two processes counting at once) is tested
/// against a real Mongo in the integration tests.
/// </remarks>
public sealed class InMemorySimulatedDocuments : ISimulatedProviderDocumentRepository
{
    private readonly object gate = new();

    private readonly Dictionary<Guid, SimulatedProviderDocument> rows = [];

    /// <summary>
    /// How many documents are kept.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return rows.Count;
            }
        }
    }

    /// <inheritdoc/>
    public Task<SimulatedProviderDocument> ReceiveAsync(SimulatedProviderDocument document, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var row = Copy(document);

            row.Checks = 0;

            if (rows.TryGetValue(document.Id, out var previous))
            {
                row.IssueDate = previous.IssueDate;
                row.FirstReceivedAt = previous.FirstReceivedAt;
            }

            rows[row.Id] = row;

            return Task.FromResult(Copy(row));
        }
    }

    /// <inheritdoc/>
    public Task<SimulatedProviderDocument?> CheckAsync(SimulatedDocumentKey key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var row = Row(key);

            if (row is null)
            {
                return Task.FromResult<SimulatedProviderDocument?>(null);
            }

            row.Checks++;

            return Task.FromResult<SimulatedProviderDocument?>(Copy(row));
        }
    }

    /// <inheritdoc/>
    public Task<SimulatedProviderDocument?> FindAsync(SimulatedDocumentKey key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(Row(key) is { } row ? Copy(row) : null);
        }
    }

    /// <inheritdoc/>
    public Task EnsureIndexesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private SimulatedProviderDocument? Row(SimulatedDocumentKey key)
    {
        if (key.Id is { } id && rows.TryGetValue(id, out var byId) && byId.Tenant == key.Tenant)
        {
            return byId;
        }

        return key.Number is null ? null : rows.Values.FirstOrDefault(x => x.Tenant == key.Tenant && x.IssuerId == key.IssuerId && x.Type == key.Type && x.Number == key.Number);
    }

    private static SimulatedProviderDocument Copy(SimulatedProviderDocument source) => new()
    {
        Id = source.Id,
        Tenant = source.Tenant,
        IssuerId = source.IssuerId,
        DocumentId = source.DocumentId,
        Type = source.Type,
        Number = source.Number,
        NumberingRange = source.NumberingRange,
        Content = source.Content,
        IssueDate = source.IssueDate,
        Outcome = source.Outcome,
        Reason = source.Reason,
        ChecksUntilResolved = source.ChecksUntilResolved,
        Checks = source.Checks,
        FirstReceivedAt = source.FirstReceivedAt,
        ReceivedAt = source.ReceivedAt,
        ExpiresAt = source.ExpiresAt
    };
}

/// <summary>
/// Builds the simulated provider for the tests.
/// </summary>
public static class SimulatedProviders
{
    /// <summary>
    /// A simulated provider over the documents given, or over its own when none are given.
    /// </summary>
    /// <param name="options">The rules of the simulation.</param>
    /// <param name="documents">Where it keeps what it receives: the same for two providers that stand for two replicas.</param>
    /// <returns>The provider.</returns>
    public static SimulatedProvider Create(SimulatedOptions? options = null, ISimulatedProviderDocumentRepository? documents = null) => new(Microsoft.Extensions.Options.Options.Create(options ?? new SimulatedOptions()), documents ?? new InMemorySimulatedDocuments(), SystemClock.Instance);
}
