namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Test.Consumers;

/// <summary>
/// When a tenant is purged, nothing of it can stay in this service.
/// </summary>
public class PurgeTenantDataHandlerTest
{
    /// <summary>
    /// The persisted types of the service that keep the tenant in a <c>Tenant</c> property.
    /// </summary>
    /// <remarks>
    /// It comes from the domain by reflection and not from a handwritten list: a new aggregate enters the test by
    /// itself, and if nobody adds it to the consumer, the test fails.
    /// </remarks>
    private static HashSet<Type> TypesWithTenant() => typeof(ElectronicDocumentAggregate).Assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && typeof(IEntityBase).IsAssignableFrom(type))
        .Where(type => type.GetProperty("Tenant", BindingFlags.Public | BindingFlags.Instance)?.PropertyType is { } property && (property == typeof(Guid) || property == typeof(Guid?)))
        .ToHashSet();

    [Fact]
    public async Task HandleAsync_TenantPurged_DeletesEveryTypeWithTenant()
    {
        // Arrange
        var repository = new Mock<IElectronicDocumentRepository>();
        var tenant = Guid.NewGuid();
        var handler = new PurgeTenantDataHandler(repository.Object, Mock.Of<ILogger<PurgeTenantDataHandler>>());

        // Act
        await handler.HandleAsync(TenantPurgedDomainEvent.Create(tenant, "Tenant"), CancellationToken.None);

        // Assert
        var purged = repository.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IRepositoryBase.DeleteByTenantAsync) && (Guid)invocation.Arguments[0] == tenant)
            .Select(invocation => invocation.Method.GetGenericArguments()[0])
            .ToHashSet();

        Assert.Empty(TypesWithTenant().Except(purged).Select(type => type.Name));
    }

    [Fact]
    public void TypesWithTenant_Domain_FindsTheAggregates()
    {
        // Act
        var types = TypesWithTenant();

        // Assert
        Assert.Contains(typeof(ElectronicDocumentAggregate), types);
    }
}
