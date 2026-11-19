using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The cash register of a point of sale ticket against a real Mongo: it is read back as it was written, and a document
/// saved before the register existed (its content does not bring the element) is still read, with the same hash.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class PointOfSaleStoredTest
{
    private readonly IServiceProvider provider;

    private readonly IElectronicDocumentRepository documents;

    public PointOfSaleStoredTest(MongoContainerFixture fixture)
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

        provider = services.BuildServiceProvider();
        documents = provider.GetRequiredService<IElectronicDocumentRepository>();
    }

    private IMongoCollection<BsonDocument> Collection()
    {
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CodeDesignPlus.Net.Mongo.Abstractions.Options.MongoOptions>>().Value;
        var client = provider.GetRequiredService<IMongoClient>();

        return client.GetDatabase(options.Database).GetCollection<BsonDocument>(nameof(ElectronicDocumentAggregate));
    }

    [Fact]
    public async Task FindAsync_PosTicket_ReadsBackItsRegisterAndItsHash()
    {
        // Arrange
        var document = TestData.Document(type: ElectronicDocumentType.PosTicket);

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var stored = await documents.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Equal((TestData.PointOfSale(), document.ContentHash), (stored.Content.PointOfSale, ElectronicDocumentAggregate.ContentHashOf(stored.Type, stored.Reference, stored.PrecededBy, stored.Content)));
    }

    [Fact]
    public async Task FindAsync_PosTicket_StoresTheRegisterInsideItsContent()
    {
        // Arrange
        var document = TestData.Document(type: ElectronicDocumentType.PosTicket);

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var raw = await Collection().Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(document.Id, GuidRepresentation.Standard))).SingleAsync();

        // Assert
        Assert.Equal("Cajera de Prueba", raw["Content"]["PointOfSale"]["CashierName"].AsString);
    }

    [Fact]
    public async Task FindAsync_DocumentSavedBeforeTheRegisterExisted_IsStillReadWithItsHash()
    {
        // Arrange: an invoice as it was saved before, whose content has no element for the register at all.
        var document = TestData.Document();
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(document.Id, GuidRepresentation.Standard));

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);
        await Collection().UpdateOneAsync(filter, Builders<BsonDocument>.Update.Unset("Content.PointOfSale"));

        var raw = await Collection().Find(filter).SingleAsync();

        // Act
        var stored = await documents.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Equal((false, null, document.ContentHash), (raw["Content"].AsBsonDocument.Contains("PointOfSale"), stored.Content.PointOfSale, ElectronicDocumentAggregate.ContentHashOf(stored.Type, stored.Reference, stored.PrecededBy, stored.Content)));
    }

    [Fact]
    public async Task FindAsync_Totals_DoNotStoreTheSubtotalTheyCompute()
    {
        // Arrange
        var document = TestData.Document(type: ElectronicDocumentType.PosTicket);

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var raw = await Collection().Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(document.Id, GuidRepresentation.Standard))).SingleAsync();

        // Assert
        Assert.False(raw["Content"]["Totals"].AsBsonDocument.Contains("Subtotal"));
    }
}
