using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using MongoDB.Bson;
using MongoDB.Driver;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

/// <summary>
/// The simulated provider against a real Mongo, as the service runs it: several replicas, each with its own provider and
/// its own connection, over the same database. What one receives the others find, the checks of all of them add up, and
/// a replica that starts later finds everything.
/// </summary>
/// <remarks>
/// A replica here is a container of services built as an entrypoint builds it (<c>AddMongo</c> and the startup of the
/// infrastructure), so the test also proves the provider gets what it needs from the container.
/// </remarks>
[Collection(MongoContainerFixture.Collection)]
public class SimulatedProviderSharedStateTest(MongoContainerFixture fixture)
{
    private const string Waits = "333333333";

    private readonly string database = $"db-ms-electronicinvoicing-{Guid.NewGuid():N}";

    private readonly MovableClock clock = new(TestData.Now);

    /// <summary>
    /// A replica of the service: its own container of services, and so its own provider, over the shared database.
    /// </summary>
    private ServiceProvider Replica(int checksUntilResolved = 2, string retention = "30.00:00:00")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:Enable"] = "true",
                ["Mongo:ConnectionString"] = fixture.ConnectionString,
                ["Mongo:Database"] = database,
                ["Mongo:RegisterHealthCheck"] = "false",
                ["Simulated:Rules:0:BuyerIdentification"] = Waits,
                ["Simulated:Rules:0:Outcome"] = nameof(SimulatedOutcome.DianUnavailable),
                ["Simulated:Rules:0:ChecksUntilResolved"] = checksUntilResolved.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Simulated:Retention"] = retention,
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddMongo<Startup>(configuration);

        new Startup().Initialize(services, configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The simulated provider of a replica, built by its container with what the startup registered.
    /// </summary>
    private static SimulatedProvider ProviderOf(ServiceProvider replica) => ActivatorUtilities.CreateInstance<SimulatedProvider>(replica);

    private static ProviderIssuer Issuer(Guid? tenant = null) => ProviderIssuer.From(TestData.EnabledIssuer(tenant: tenant));

    private static ProviderNumberingRange Range() => new("technical-key-of-test", "POS", "18760000001", 1, 5_000_000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    private static ProviderDocument Ticket(Guid id, string buyer = Waits, long consecutive = 1) => new(id, ElectronicDocumentType.PosTicket, new DocumentNumber("POS", consecutive), Range(), TestData.PosTicketContent(buyer), "key", new LocalDate(2026, 11, 4));

    private static ProviderDocumentLookup Lookup(string? providerDocumentId, DocumentNumber? number = null) => new(ElectronicDocumentType.PosTicket, providerDocumentId, number, "key");

    private IMongoCollection<BsonDocument> Rows(ServiceProvider replica) => replica.GetRequiredService<IMongoClient>().GetDatabase(database).GetCollection<BsonDocument>(nameof(SimulatedProviderDocument));

    [Fact]
    public async Task GetStateAsync_DocumentReceivedByAnotherReplica_IsFound()
    {
        // Arrange
        using var first = Replica();
        using var second = Replica();

        var issuer = Issuer();
        var submission = await ProviderOf(first).SubmitAsync(issuer, Ticket(Guid.NewGuid()), CancellationToken.None);

        // Act
        var state = await ProviderOf(second).GetStateAsync(issuer, Lookup(submission.ProviderDocumentId), CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.DianUnavailable, submission.ProviderDocumentId), (state?.Status, state?.ProviderDocumentId));
    }

    [Fact]
    public async Task GetStateAsync_ChecksSplitBetweenReplicas_AddUpUntilTheDocumentIsAccepted()
    {
        // Arrange: the production case, where each check lands in whichever replica is free.
        using var first = Replica(checksUntilResolved: 3);
        using var second = Replica(checksUntilResolved: 3);

        var issuer = Issuer();
        var submission = await ProviderOf(first).SubmitAsync(issuer, Ticket(Guid.NewGuid()), CancellationToken.None);
        var lookup = Lookup(submission.ProviderDocumentId);

        // Act
        var one = await ProviderOf(second).GetStateAsync(issuer, lookup, CancellationToken.None);
        var two = await ProviderOf(first).GetStateAsync(issuer, lookup, CancellationToken.None);
        var three = await ProviderOf(second).GetStateAsync(issuer, lookup, CancellationToken.None);

        // Assert
        Assert.Equal((ProviderDocumentStatus.DianUnavailable, ProviderDocumentStatus.DianUnavailable, ProviderDocumentStatus.Accepted), (one!.Status, two!.Status, three!.Status));
    }

    [Fact]
    public async Task GetStateAsync_TwoReplicasCheckingAtOnce_CountEveryCheck()
    {
        // Arrange
        const int checksOfEach = 40;

        using var first = Replica(checksUntilResolved: 1000);
        using var second = Replica(checksUntilResolved: 1000);

        var issuer = Issuer();
        var submission = await ProviderOf(first).SubmitAsync(issuer, Ticket(Guid.NewGuid()), CancellationToken.None);
        var lookup = Lookup(submission.ProviderDocumentId);
        var providers = new[] { ProviderOf(first), ProviderOf(second) };

        // Act
        await Task.WhenAll(Enumerable.Range(0, checksOfEach * 2).Select(i => Task.Run(() => providers[i % 2].GetStateAsync(issuer, lookup, CancellationToken.None))));

        // Assert
        var row = await Rows(first).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal(checksOfEach * 2, row[nameof(SimulatedProviderDocument.Checks)].AsInt32);
    }

    [Fact]
    public async Task GetFilesAsync_ReplicaStartedAfterTheDocumentArrived_HandsOverTheSameFiles()
    {
        // Arrange: the replica that received the document is gone, as after a restart.
        var issuer = Issuer();
        var id = Guid.NewGuid();
        string? providerDocumentId;
        ProviderFiles before;

        using (var gone = Replica(checksUntilResolved: 0))
        {
            providerDocumentId = (await ProviderOf(gone).SubmitAsync(issuer, Ticket(id), CancellationToken.None)).ProviderDocumentId;
            before = await ProviderOf(gone).GetFilesAsync(issuer, Lookup(providerDocumentId), CancellationToken.None);
        }

        using var restarted = Replica(checksUntilResolved: 0);

        // Act
        var after = await ProviderOf(restarted).GetFilesAsync(issuer, Lookup(providerDocumentId), CancellationToken.None);

        // Assert: the register, the resolution and every line come back as they went, or the PDF would differ.
        Assert.Equal((true, true, "POS1.pdf"), (before.Pdf!.Content.SequenceEqual(after.Pdf!.Content), before.Xml!.Content.SequenceEqual(after.Xml!.Content), after.Pdf.Name));
    }

    [Fact]
    public async Task GetBuyerNoticeAsync_ReplicaStartedAfterTheDocumentArrived_StillWritesTheEmailOfTheBuyer()
    {
        // Arrange
        var issuer = Issuer();
        string? providerDocumentId;

        using (var gone = Replica())
        {
            providerDocumentId = (await ProviderOf(gone).SubmitAsync(issuer, Ticket(Guid.NewGuid(), buyer: "900000001"), CancellationToken.None)).ProviderDocumentId;
        }

        using var restarted = Replica();

        // Act
        var notice = await ProviderOf(restarted).GetBuyerNoticeAsync(issuer, Lookup(providerDocumentId), CancellationToken.None);

        // Assert
        Assert.Equal("comprador@example.com", notice?.To);
    }

    [Fact]
    public async Task GetStateAsync_DocumentNeverReceived_IsNotFound()
    {
        // Arrange: the provider has other documents, but not this one.
        using var replica = Replica();

        var issuer = Issuer();

        await ProviderOf(replica).SubmitAsync(issuer, Ticket(Guid.NewGuid()), CancellationToken.None);

        // Act
        var state = await ProviderOf(replica).GetStateAsync(issuer, Lookup(Guid.NewGuid().ToString(), new DocumentNumber("POS", 2)), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetStateAsync_IdThatIsNotOfTheSimulation_IsNotFound()
    {
        // Arrange
        using var replica = Replica();

        // Act
        var state = await ProviderOf(replica).GetStateAsync(Issuer(), Lookup("not-an-id-of-the-simulation"), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetStateAsync_DocumentOfAnotherTenant_IsNotFoundByItsIdNorByItsNumber()
    {
        // Arrange
        using var replica = Replica();

        var submission = await ProviderOf(replica).SubmitAsync(Issuer(), Ticket(Guid.NewGuid()), CancellationToken.None);

        // Act
        var state = await ProviderOf(replica).GetStateAsync(Issuer(Guid.NewGuid()), Lookup(submission.ProviderDocumentId, new DocumentNumber("POS", 1)), CancellationToken.None);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task GetStateAsync_DocumentOfAnotherTenant_DoesNotCountACheck()
    {
        // Arrange
        using var replica = Replica();

        var submission = await ProviderOf(replica).SubmitAsync(Issuer(), Ticket(Guid.NewGuid()), CancellationToken.None);

        // Act
        await ProviderOf(replica).GetStateAsync(Issuer(Guid.NewGuid()), Lookup(submission.ProviderDocumentId, new DocumentNumber("POS", 1)), CancellationToken.None);

        // Assert
        var row = await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal(0, row[nameof(SimulatedProviderDocument.Checks)].AsInt32);
    }

    [Fact]
    public async Task GetStateAsync_SendWhoseAnswerWasLost_IsFoundByItsNumberFromAnotherReplica()
    {
        // Arrange: the service never learned the id the provider gave, so it asks by the number.
        using var first = Replica();
        using var second = Replica();

        var issuer = Issuer();
        var submission = await ProviderOf(first).SubmitAsync(issuer, Ticket(Guid.NewGuid(), consecutive: 7), CancellationToken.None);

        // Act
        var state = await ProviderOf(second).GetStateAsync(issuer, Lookup(null, new DocumentNumber("POS", 7)), CancellationToken.None);

        // Assert
        Assert.Equal(submission.ProviderDocumentId, state?.ProviderDocumentId);
    }

    [Fact]
    public async Task GetFilesAsync_AskedSeveralTimes_DoesNotCountAsChecks()
    {
        // Arrange: only asking for the state moves a document that waits.
        using var replica = Replica(checksUntilResolved: 1);

        var issuer = Issuer();
        var submission = await ProviderOf(replica).SubmitAsync(issuer, Ticket(Guid.NewGuid()), CancellationToken.None);
        var lookup = Lookup(submission.ProviderDocumentId);

        // Act
        await ProviderOf(replica).GetFilesAsync(issuer, lookup, CancellationToken.None);
        await ProviderOf(replica).GetBuyerNoticeAsync(issuer, lookup, CancellationToken.None);

        // Assert
        var row = await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal(0, row[nameof(SimulatedProviderDocument.Checks)].AsInt32);
    }

    [Fact]
    public async Task SubmitAsync_SameDocumentSentAgain_StartsItsChecksOver()
    {
        // Arrange
        using var first = Replica();
        using var second = Replica();

        var issuer = Issuer();
        var id = Guid.NewGuid();
        var submission = await ProviderOf(first).SubmitAsync(issuer, Ticket(id), CancellationToken.None);
        var lookup = Lookup(submission.ProviderDocumentId);

        await ProviderOf(first).GetStateAsync(issuer, lookup, CancellationToken.None);

        // Act
        await ProviderOf(second).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        // Assert: one check after the new send is not the second one.
        var state = await ProviderOf(first).GetStateAsync(issuer, lookup, CancellationToken.None);

        Assert.Equal(ProviderDocumentStatus.DianUnavailable, state!.Status);
    }

    [Fact]
    public async Task SubmitAsync_SameDocumentSentAgainAnotherDay_KeepsOneRowWithItsFirstIssueDateAndItsNewContent()
    {
        // Arrange
        using var first = Replica();
        using var second = Replica();

        var issuer = Issuer();
        var id = Guid.NewGuid();

        await ProviderOf(first).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        clock.Now = TestData.Now + Duration.FromDays(2);

        // Act: the correction of a rejected document reaches another replica two days later, now for a buyer with no rule.
        var again = await ProviderOf(second).SubmitAsync(issuer, Ticket(id, buyer: "900000001"), CancellationToken.None);

        // Assert
        var row = await Rows(first).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal((ProviderDocumentStatus.Accepted, new LocalDate(2026, 11, 4), "900000001"), (again.State.Status, again.State.IssueDate, row["Content"]["Buyer"]["IdentificationNumber"].AsString));
    }

    [Fact]
    public async Task SubmitAsync_TwoReplicasReceiveTheSameDocumentAtOnce_KeepOneRowAndNeitherFails()
    {
        // Arrange
        using var first = Replica();
        using var second = Replica();

        var issuer = Issuer();
        var providers = new[] { ProviderOf(first), ProviderOf(second) };

        // Act: many documents, each sent by the two replicas at the same time, so some pair collides creating its row.
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();

        var submissions = await Task.WhenAll(ids.SelectMany((id, i) => providers.Select(provider => Task.Run(() => provider.SubmitAsync(issuer, Ticket(id, consecutive: i + 1), CancellationToken.None)))));

        // Assert
        Assert.Equal((ids.Count, ids.Count), (submissions.Select(x => x.ProviderDocumentId).Distinct().Count(), (int)await Rows(first).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)));
    }

    [Fact]
    public async Task ReceiveAsync_EveryMember_IsReadBackAsItWasWritten()
    {
        // Arrange
        using var replica = Replica();

        var documents = replica.GetRequiredService<ISimulatedProviderDocumentRepository>();

        var received = new SimulatedProviderDocument
        {
            Id = Guid.NewGuid(),
            Tenant = TestData.Tenant,
            IssuerId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = ElectronicDocumentType.PosTicket,
            Number = new DocumentNumber("POS", 15),
            NumberingRange = SimulatedNumberingRange.Of(Range()),
            Content = TestData.PosTicketContent("222222222"),
            IssueDate = new LocalDate(2026, 11, 4),
            Outcome = SimulatedOutcome.Rejected,
            Reason = "Rechazo simulado.",
            ChecksUntilResolved = 5,
            FirstReceivedAt = TestData.Now,
            ReceivedAt = TestData.Now + Duration.FromMinutes(3),
            ExpiresAt = TestData.Now + Duration.FromDays(30)
        };

        // Act
        await documents.ReceiveAsync(received, CancellationToken.None);

        var stored = await documents.FindAsync(new SimulatedDocumentKey(received.Tenant, received.IssuerId, received.Id, received.Type, null), CancellationToken.None);

        // Assert
        Assert.Equal(received.ToJson(), stored.ToJson());
    }

    [Fact]
    public async Task SubmitAsync_DocumentSentAgainLater_StoresWhenItArrivedLastAsADate()
    {
        // Arrange: a date of the database, and not a text, is what an expiry by age of the rows can be put on.
        using var replica = Replica();

        var issuer = Issuer();
        var id = Guid.NewGuid();

        await ProviderOf(replica).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        clock.Now = TestData.Now + Duration.FromHours(5);

        // Act
        await ProviderOf(replica).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        // Assert
        var row = await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal((TestData.Now.ToDateTimeUtc(), (TestData.Now + Duration.FromHours(5)).ToDateTimeUtc()), (row[nameof(SimulatedProviderDocument.FirstReceivedAt)].ToUniversalTime(), row[nameof(SimulatedProviderDocument.ReceivedAt)].ToUniversalTime()));
    }

    [Fact]
    public async Task SubmitAsync_Retention_StoresWhenTheRowExpiresCountedFromItsLastSend()
    {
        // Arrange
        using var replica = Replica(retention: "10.00:00:00");

        var issuer = Issuer();
        var id = Guid.NewGuid();

        await ProviderOf(replica).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        var first = (await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())[nameof(SimulatedProviderDocument.ExpiresAt)].ToUniversalTime();

        clock.Now = TestData.Now + Duration.FromDays(3);

        // Act
        await ProviderOf(replica).SubmitAsync(issuer, Ticket(id), CancellationToken.None);

        // Assert
        var second = (await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())[nameof(SimulatedProviderDocument.ExpiresAt)].ToUniversalTime();

        Assert.Equal(((TestData.Now + Duration.FromDays(10)).ToDateTimeUtc(), (TestData.Now + Duration.FromDays(13)).ToDateTimeUtc()), (first, second));
    }

    [Fact]
    public async Task SubmitAsync_RetentionOfZero_StoresNoExpiry()
    {
        // Arrange
        using var replica = Replica(retention: "00:00:00");

        // Act
        await ProviderOf(replica).SubmitAsync(Issuer(), Ticket(Guid.NewGuid()), CancellationToken.None);

        // Assert
        var row = await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.True(row[nameof(SimulatedProviderDocument.ExpiresAt)].IsBsonNull);
    }

    [Fact]
    public async Task EnsureIndexesAsync_RunByTwoReplicas_LeavesOneIndexThatExpiresEachRowAtItsDate()
    {
        // Arrange
        using var first = Replica();
        using var second = Replica();

        // Act: every entrypoint creates it when it starts.
        await first.GetRequiredService<ISimulatedProviderDocumentRepository>().EnsureIndexesAsync(CancellationToken.None);
        await second.GetRequiredService<ISimulatedProviderDocumentRepository>().EnsureIndexesAsync(CancellationToken.None);

        // Assert
        var indexes = await (await Rows(first).Indexes.ListAsync()).ToListAsync();
        var expiry = indexes.Single(x => x["name"] == SimulatedProviderDocumentRepository.ExpiryIndexName);

        Assert.Equal((2, nameof(SimulatedProviderDocument.ExpiresAt), 0), (indexes.Count, expiry["key"].AsBsonDocument.Names.Single(), expiry["expireAfterSeconds"].ToInt32()));
    }

    [Fact]
    public async Task GetStateAsync_DocumentWhoseRetentionPassed_IsDeletedByTheDatabaseAndNotFoundAnyMore()
    {
        // Arrange: the database looks for expired rows every second instead of every minute, so the test does not wait.
        using var replica = Replica(retention: "1.00:00:00");

        await replica.GetRequiredService<IMongoClient>().GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument { { "setParameter", 1 }, { "ttlMonitorSleepSecs", 1 } });
        await replica.GetRequiredService<ISimulatedProviderDocumentRepository>().EnsureIndexesAsync(CancellationToken.None);

        var issuer = Issuer();

        // One arrived two days before the real time, so its day of retention passed; the other arrives now.
        clock.Now = SystemClock.Instance.GetCurrentInstant() - Duration.FromDays(2);

        var expired = await ProviderOf(replica).SubmitAsync(issuer, Ticket(Guid.NewGuid(), consecutive: 1), CancellationToken.None);

        clock.Now = SystemClock.Instance.GetCurrentInstant();

        var recent = await ProviderOf(replica).SubmitAsync(issuer, Ticket(Guid.NewGuid(), consecutive: 2), CancellationToken.None);

        // Act
        var deadline = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < deadline && await Rows(replica).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty) > 1)
        {
            await Task.Delay(250);
        }

        // Assert
        var gone = await ProviderOf(replica).GetStateAsync(issuer, Lookup(expired.ProviderDocumentId, new DocumentNumber("POS", 1)), CancellationToken.None);
        var kept = await ProviderOf(replica).GetStateAsync(issuer, Lookup(recent.ProviderDocumentId), CancellationToken.None);

        Assert.Equal((null, recent.ProviderDocumentId), (gone?.ProviderDocumentId, kept?.ProviderDocumentId));
    }

    [Fact]
    public async Task DeleteByTenantAsync_TenantPurged_TakesItsDocumentsOfTheSimulatedProviderAndLeavesTheOthers()
    {
        // Arrange
        using var replica = Replica();

        var purged = Guid.NewGuid();
        var kept = Guid.NewGuid();

        await ProviderOf(replica).SubmitAsync(Issuer(purged), Ticket(Guid.NewGuid()), CancellationToken.None);
        await ProviderOf(replica).SubmitAsync(Issuer(kept), Ticket(Guid.NewGuid()), CancellationToken.None);

        // Act: as the consumer of the purge does, through the repository of the documents.
        var deleted = await replica.GetRequiredService<IElectronicDocumentRepository>().DeleteByTenantAsync<SimulatedProviderDocument>(purged, CancellationToken.None);

        // Assert
        var left = await Rows(replica).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal((1L, kept), (deleted, left[nameof(SimulatedProviderDocument.Tenant)].AsGuid));
    }

    /// <summary>
    /// A clock the test moves, shared by the replicas as the real time is.
    /// </summary>
    private sealed class MovableClock(Instant now) : IClock
    {
        public Instant Now { get; set; } = now;

        public Instant GetCurrentInstant() => Now;
    }
}
