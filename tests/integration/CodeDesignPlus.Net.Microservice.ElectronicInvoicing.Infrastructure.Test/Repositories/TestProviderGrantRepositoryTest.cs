using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The permissions to issue with a test provider against a real Mongo: one document per tenant however many operators
/// grant at once, every field read back as it was written, and gone when the tenant is purged.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class TestProviderGrantRepositoryTest
{
    private readonly ITestProviderGrantRepository grants;

    public TestProviderGrantRepositoryTest(MongoContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:Enable"] = "true",
                ["Mongo:ConnectionString"] = fixture.ConnectionString,
                ["Mongo:Database"] = $"db-ms-electronicinvoicing-{Guid.NewGuid():N}",
                ["Mongo:RegisterHealthCheck"] = "false",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddMongo<Startup>(configuration);

        grants = services.BuildServiceProvider().GetRequiredService<ITestProviderGrantRepository>();
    }

    /// <summary>
    /// Grants as the command does: the permission is created when the tenant has none, and granted again otherwise.
    /// </summary>
    private Task<bool> GrantAsync(Guid tenant, Guid by, Instant now) => grants.UpdateWithRetryAsync(
        async () => await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None) ?? TestProviderGrantAggregate.Grant(tenant, by, now),
        grant => grant.Version == 0 || grant.GrantAgain(by, now),
        CancellationToken.None);

    private Task<bool> RevokeAsync(Guid tenant, Guid by, Instant now) => grants.UpdateWithRetryAsync(
        () => grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None),
        grant => grant.Revoke(by, now),
        CancellationToken.None);

    [Fact]
    public async Task UpdateWithRetryAsync_NewPermission_ReadsBackWhoGrantedItAndWhen()
    {
        // Arrange
        var tenant = Guid.NewGuid();

        // Act
        await GrantAsync(tenant, TestData.User, TestData.Now);

        // Assert
        var stored = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None);

        Assert.Equal((tenant, true, TestData.User, TestData.Now, (Guid?)null, (Instant?)null), (stored.Tenant, stored.Granted, stored.GrantedBy, stored.GrantedAt, stored.RevokedBy, stored.RevokedAt));
    }

    [Fact]
    public async Task UpdateWithRetryAsync_Revoked_ReadsBackWhoRevokedItAndKeepsWhoGrantedIt()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var later = TestData.Now + Duration.FromHours(3);

        await GrantAsync(tenant, TestData.User, TestData.Now);

        // Act
        await RevokeAsync(tenant, TestData.SystemUser, later);

        // Assert
        var stored = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None);

        Assert.Equal((false, TestData.User, (Guid?)TestData.SystemUser, (Instant?)later), (stored.Granted, stored.GrantedBy, stored.RevokedBy, stored.RevokedAt));
    }

    [Fact]
    public async Task UpdateWithRetryAsync_RevokedAndGrantedAgain_IsGrantedWithoutTheRevocation()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var later = TestData.Now + Duration.FromHours(3);

        await GrantAsync(tenant, TestData.User, TestData.Now);
        await RevokeAsync(tenant, TestData.SystemUser, later);

        // Act
        await GrantAsync(tenant, TestData.SystemUser, later + Duration.FromHours(1));

        // Assert
        var stored = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None);

        Assert.Equal((true, TestData.SystemUser, (Guid?)null, (Instant?)null), (stored.Granted, stored.GrantedBy, stored.RevokedBy, stored.RevokedAt));
    }

    [Fact]
    public async Task UpdateWithRetryAsync_SeveralOperatorsGrantAtOnce_LeavesASinglePermissionGranted()
    {
        // Arrange
        var tenant = Guid.NewGuid();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GrantAsync(tenant, TestData.User, TestData.Now)));

        // Assert
        var stored = await grants.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(tenant), CancellationToken.None);

        Assert.Equal((true, tenant), (stored.Granted, stored.Tenant));
    }

    [Fact]
    public async Task DeleteByTenantAsync_PurgedTenant_LeavesNoPermissionAndKeepsTheOthers()
    {
        // Arrange
        var purged = Guid.NewGuid();
        var kept = Guid.NewGuid();

        await GrantAsync(purged, TestData.User, TestData.Now);
        await GrantAsync(kept, TestData.User, TestData.Now);

        // Act
        var deleted = await grants.DeleteByTenantAsync<TestProviderGrantAggregate>(purged, CancellationToken.None);

        // Assert
        var exists = (await grants.ExistsAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(purged), CancellationToken.None), await grants.ExistsAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(kept), CancellationToken.None));

        Assert.Equal((1L, false, true), (deleted, exists.Item1, exists.Item2));
    }
}
