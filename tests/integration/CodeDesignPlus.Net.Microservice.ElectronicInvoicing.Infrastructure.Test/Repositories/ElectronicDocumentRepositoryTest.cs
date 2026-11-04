namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The documents against a real Mongo: one per id however many requests create it at once.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class ElectronicDocumentRepositoryTest
{
    private readonly IElectronicDocumentRepository repository;

    public ElectronicDocumentRepositoryTest(MongoContainerFixture fixture)
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

        this.repository = services.BuildServiceProvider().GetRequiredService<IElectronicDocumentRepository>();
    }

    [Fact]
    public async Task CreateIfAbsentAsync_TwoRequestsWithTheSameId_SavesASingleDocument()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        Task<bool> Create() => repository.CreateIfAbsentAsync(ElectronicDocumentAggregate.Create(id, tenant, "orders:order:1", Guid.NewGuid()), CancellationToken.None);

        // Act
        var results = await Task.WhenAll(Create(), Create());

        // Assert
        Assert.Single(results, written => written);
    }

    [Fact]
    public async Task FindAsync_SavedDocument_ReadsBackItsReference()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var document = ElectronicDocumentAggregate.Create(Guid.NewGuid(), tenant, "orders:order:2", Guid.NewGuid());

        await repository.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var stored = await repository.FindAsync<ElectronicDocumentAggregate>(document.Id, tenant, CancellationToken.None);

        // Assert
        Assert.Equal((tenant, "orders:order:2"), (stored.Tenant, stored.Reference));
    }
}
