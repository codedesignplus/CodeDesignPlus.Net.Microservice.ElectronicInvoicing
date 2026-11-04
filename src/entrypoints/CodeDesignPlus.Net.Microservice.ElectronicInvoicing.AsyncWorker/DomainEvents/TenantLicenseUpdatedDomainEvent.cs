namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.DomainEvents;

/// <summary>
/// ms-tenants changed the effective license of a tenant: its own attributes combined with those of its packages.
/// </summary>
/// <remarks>
/// Only what is used is declared: the license with its end and its attributes. The rest of the event is ignored when it
/// is deserialized. The names of the constructor are those of the properties, or the value would travel and be lost.
/// </remarks>
/// <param name="aggregateId">The id of the tenant.</param>
/// <param name="license">The effective license.</param>
/// <param name="updatedBy">Who changed it.</param>
/// <param name="eventId">The id of the event.</param>
/// <param name="occurredAt">When the event occurred.</param>
/// <param name="metadata">The metadata of the event.</param>
[EventKey("TenantAggregate", 1, "TenantLicenseUpdatedDomainEvent", "ms-tenants", "codedesignplus")]
public class TenantLicenseUpdatedDomainEvent(
    Guid aggregateId,
    TenantLicense license,
    Guid updatedBy,
    Guid? eventId = null,
    Instant? occurredAt = null,
    Dictionary<string, object>? metadata = null
) : DomainEvent(aggregateId, eventId, occurredAt, metadata)
{
    /// <summary>
    /// The effective license.
    /// </summary>
    public TenantLicense License { get; } = license;

    /// <summary>
    /// Who changed it.
    /// </summary>
    public Guid UpdatedBy { get; } = updatedBy;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="aggregateId">The id of the tenant.</param>
    /// <param name="license">The effective license.</param>
    /// <param name="updatedBy">Who changed it.</param>
    /// <returns>The new event.</returns>
    public static TenantLicenseUpdatedDomainEvent Create(Guid aggregateId, TenantLicense license, Guid updatedBy) => new(aggregateId, license, updatedBy);
}

/// <summary>
/// The license of a tenant as ms-tenants publishes it; only what this service reads.
/// </summary>
/// <param name="Id">The id of the license.</param>
/// <param name="StartDate">When the license started.</param>
/// <param name="EndDate">When the license ends.</param>
/// <param name="Metadata">The effective attributes: those of the license combined with those of its packages.</param>
public sealed record TenantLicense(Guid Id, Instant StartDate, Instant EndDate, Dictionary<string, string> Metadata);
