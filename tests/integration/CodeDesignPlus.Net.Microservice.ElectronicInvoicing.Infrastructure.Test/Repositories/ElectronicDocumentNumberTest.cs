using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Repositories;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using MongoDB.Driver;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The numbers a caller gives, against a real Mongo: the unique index keeps two documents of an issuer and a type from
/// carrying the same number however many requests write at once, and the watch reads the highest one used.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class ElectronicDocumentNumberTest
{
    private readonly IServiceProvider provider;

    private readonly IElectronicDocumentRepository documents;

    public ElectronicDocumentNumberTest(MongoContainerFixture fixture)
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

    private static ElectronicDocumentAggregate Numbered(string prefix, long consecutive, ElectronicDocumentType type = ElectronicDocumentType.Invoice, Guid? tenant = null)
    {
        var document = TestData.Document(type: type, tenant: tenant);

        document.AssignNumber(new DocumentNumber(prefix, consecutive), null);

        return document;
    }

    [Fact]
    public async Task CreateIfAbsentAsync_TwoDocumentsWithTheSameNumberAtOnce_SavesOnlyOne()
    {
        // Arrange
        await documents.EnsureIndexesAsync(CancellationToken.None);

        Task<bool> Create() => documents.CreateIfAbsentAsync(Numbered("FAC", 12), CancellationToken.None);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create()));

        // Assert
        Assert.Single(results, written => written);
    }

    [Fact]
    public async Task CreateIfAbsentAsync_SameNumberOfAnotherTypeOrIssuer_SavesBoth()
    {
        // Arrange
        await documents.EnsureIndexesAsync(CancellationToken.None);

        await documents.CreateIfAbsentAsync(Numbered("FAC", 12), CancellationToken.None);

        // Act
        var otherType = await documents.CreateIfAbsentAsync(Numbered("FAC", 12, ElectronicDocumentType.DebitNote), CancellationToken.None);
        var otherIssuer = await documents.CreateIfAbsentAsync(Numbered("FAC", 12, tenant: Guid.NewGuid()), CancellationToken.None);

        // Assert
        Assert.Equal((true, true), (otherType, otherIssuer));
    }

    [Fact]
    public async Task CreateIfAbsentAsync_SeveralDocumentsWithoutANumber_SavesThemAll()
    {
        // Arrange: drafts waiting for the number this service gives them do not collide on an empty number.
        await documents.EnsureIndexesAsync(CancellationToken.None);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => documents.CreateIfAbsentAsync(TestData.Document(), CancellationToken.None)));

        // Assert
        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task UpdateWithRetryAsync_NumberThisServiceGivesThatACallerAlreadyUsed_IsRefused()
    {
        // Arrange: the number goes into the draft after it is saved; the index refuses it as well.
        await documents.EnsureIndexesAsync(CancellationToken.None);
        await documents.CreateIfAbsentAsync(Numbered("SETP", 990000000), CancellationToken.None);

        var draft = TestData.Document();

        await documents.CreateIfAbsentAsync(draft, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => documents.UpdateWithRetryAsync(
            () => documents.FindAsync<ElectronicDocumentAggregate>(draft.Id, CancellationToken.None),
            fresh => fresh.AssignNumber(new DocumentNumber("SETP", 990000000), null),
            CancellationToken.None));

        // Assert
        Assert.Equal(ServerErrorCategory.DuplicateKey, Assert.IsType<MongoWriteException>(exception).WriteError.Category);
    }

    [Fact]
    public async Task EnsureIndexesAsync_RunTwice_KeepsASingleUniqueIndex()
    {
        // Act
        await Task.WhenAll(documents.EnsureIndexesAsync(CancellationToken.None), documents.EnsureIndexesAsync(CancellationToken.None));

        // Assert
        var indexes = await (await Collection().Indexes.ListAsync()).ToListAsync();

        Assert.True(indexes.Single(x => x["name"] == ElectronicDocumentRepository.NumberIndexName)["unique"].AsBoolean);
    }

    [Fact]
    public async Task IsNumberTakenAsync_NumberOfTheIssuerAndType_IsTakenAndAnotherIsNot()
    {
        // Arrange
        var saved = Numbered("FAC", 12);

        await documents.CreateIfAbsentAsync(saved, CancellationToken.None);

        // Act
        var taken = await documents.IsNumberTakenAsync(saved.IssuerId, ElectronicDocumentType.Invoice, new DocumentNumber("FAC", 12), CancellationToken.None);
        var free = await documents.IsNumberTakenAsync(saved.IssuerId, ElectronicDocumentType.Invoice, new DocumentNumber("FAC", 13), CancellationToken.None);
        var otherType = await documents.IsNumberTakenAsync(saved.IssuerId, ElectronicDocumentType.CreditNote, new DocumentNumber("FAC", 12), CancellationToken.None);

        // Assert
        Assert.Equal((true, false, false), (taken, free, otherType));
    }

    [Fact]
    public async Task FindHighestConsecutiveAsync_DocumentsInsideAndOutsideTheRange_ReturnsTheHighestInside()
    {
        // Arrange
        foreach (var consecutive in new long[] { 5, 40, 17, 900 })
        {
            await documents.CreateIfAbsentAsync(Numbered("FAC", consecutive), CancellationToken.None);
        }

        await documents.CreateIfAbsentAsync(Numbered("OTRO", 99), CancellationToken.None);

        // Act
        var highest = await documents.FindHighestConsecutiveAsync(IssuerAggregate.IdFor(TestData.Tenant), ElectronicDocumentType.Invoice, "FAC", 1, 100, CancellationToken.None);

        // Assert
        Assert.Equal(40L, highest);
    }

    [Fact]
    public async Task FindHighestConsecutiveAsync_NoDocumentInTheRange_ReturnsNull()
    {
        // Act
        var highest = await documents.FindHighestConsecutiveAsync(IssuerAggregate.IdFor(TestData.Tenant), ElectronicDocumentType.Invoice, "FAC", 1, 100, CancellationToken.None);

        // Assert
        Assert.Null(highest);
    }

    private IMongoCollection<MongoDB.Bson.BsonDocument> Collection()
    {
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CodeDesignPlus.Net.Mongo.Abstractions.Options.MongoOptions>>().Value;
        var client = provider.GetRequiredService<IMongoClient>();

        return client.GetDatabase(options.Database).GetCollection<MongoDB.Bson.BsonDocument>(nameof(ElectronicDocumentAggregate));
    }
}
