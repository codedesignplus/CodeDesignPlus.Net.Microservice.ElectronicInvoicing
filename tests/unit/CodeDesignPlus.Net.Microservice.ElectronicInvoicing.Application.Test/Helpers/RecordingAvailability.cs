using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

/// <summary>
/// An announcer that only remembers what it was asked: which tenants were announced, and the events it built and
/// published.
/// </summary>
public sealed class RecordingAvailability : IIssuingAvailabilityAnnouncer
{
    /// <summary>
    /// The tenants whose state was read and published.
    /// </summary>
    public List<Guid> Announced { get; } = [];

    /// <summary>
    /// The tenants whose state was asked to be published because the permission of their test provider changed.
    /// </summary>
    public List<Guid> AnnouncedForTestProvider { get; } = [];

    /// <summary>
    /// The events built from a quota already read.
    /// </summary>
    public List<IssuingAvailabilityChangedDomainEvent> Built { get; } = [];

    /// <summary>
    /// The events published one by one.
    /// </summary>
    public List<IDomainEvent> Published { get; } = [];

    /// <inheritdoc/>
    public Task<IDomainEvent> BuildAsync(Guid tenant, DocumentQuotaAggregate? quota, Instant now, CancellationToken cancellationToken)
    {
        var built = IssuingAvailabilityAnnouncer.Of(tenant, null, true, quota?.StandingAt(now) ?? QuotaStanding.None);

        Built.Add(built);

        return Task.FromResult<IDomainEvent>(built);
    }

    /// <inheritdoc/>
    public Task PublishAsync(IDomainEvent availability, CancellationToken cancellationToken)
    {
        Published.Add(availability);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AnnounceAsync(Guid tenant, CancellationToken cancellationToken)
    {
        Announced.Add(tenant);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AnnounceForTestProviderAsync(Guid tenant, CancellationToken cancellationToken)
    {
        AnnouncedForTestProvider.Add(tenant);

        return Task.CompletedTask;
    }
}
