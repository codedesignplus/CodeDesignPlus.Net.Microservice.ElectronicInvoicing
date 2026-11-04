using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The repositories against a real Mongo: one document per id however many requests create it at once, every
/// field read back as it was written, and no number nor unit of quota lost when two writers race.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class ElectronicDocumentRepositoryTest
{
    private readonly IServiceProvider provider;

    private readonly IElectronicDocumentRepository documents;

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

        provider = services.BuildServiceProvider();
        documents = provider.GetRequiredService<IElectronicDocumentRepository>();
    }

    private static ElectronicDocumentAggregate Sent(Instant at)
    {
        var document = TestData.Document();

        document.AssignNumber(new DocumentNumber("SETP", 1), Guid.NewGuid());
        document.Claim(TestData.Now, TestData.Now + Duration.FromMinutes(3));
        document.MarkSent("provider-1", "Procesando", null, false, at, at + Duration.FromMinutes(1));

        return document;
    }

    [Fact]
    public async Task CreateIfAbsentAsync_TwoRequestsWithTheSameId_SavesASingleDocument()
    {
        // Arrange
        var id = Guid.NewGuid();

        Task<bool> Create() => documents.CreateIfAbsentAsync(TestData.Document(id), CancellationToken.None);

        // Act
        var results = await Task.WhenAll(Create(), Create());

        // Assert
        Assert.Single(results, written => written);
    }

    [Fact]
    public async Task FindAsync_SavedDocument_ReadsBackItsContentAndHash()
    {
        // Arrange
        var document = TestData.Document(content: TestData.InvoiceContent() with { Prepayments = [new Prepayment("credit-1", 100_000, new LocalDate(2026, 11, 1))], Totals = TestData.Totals() with { PrepaymentAmount = 100_000, PayableAmount = 2_280_000 } });

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var stored = await documents.FindAsync<ElectronicDocumentAggregate>(document.Id, TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Equal((document.ContentHash, ElectronicDocumentAggregate.ContentHashOf(stored.Type, stored.Reference, stored.PrecededBy, stored.Content), 2m, new LocalDate(2026, 11, 1)), (stored.ContentHash, stored.ContentHash, stored.Content.Lines[0].Quantity, stored.Content.Prepayments[0].ReceivedOn));
    }

    [Fact]
    public async Task FindDueAsync_DraftsAndSentDue_ReturnsThemButNotTheStalledNorTheFuture()
    {
        // Arrange
        var due = TestData.Document(tenant: Guid.NewGuid());
        var stalled = TestData.Document(tenant: Guid.NewGuid());
        var future = Sent(TestData.Now + Duration.FromDays(3650));

        stalled.Stall(TestData.Now);

        foreach (var document in new[] { due, stalled, future })
        {
            await documents.CreateIfAbsentAsync(document, CancellationToken.None);
        }

        // Act
        var found = await documents.FindDueAsync(TestData.Now + Duration.FromMinutes(1), 1000, CancellationToken.None);

        // Assert
        Assert.Equal((true, false, false), (found.Any(x => x.Id == due.Id), found.Any(x => x.Id == stalled.Id), found.Any(x => x.Id == future.Id)));
    }

    [Fact]
    public async Task CountFirstSentBetweenAsync_DocumentsOfEveryTenant_CountsThoseSentInThePeriod()
    {
        // Arrange
        var from = Instant.FromUtc(2030, 1, 1, 0, 0);

        await documents.CreateIfAbsentAsync(Sent(from + Duration.FromDays(1)), CancellationToken.None);
        await documents.CreateIfAbsentAsync(Sent(from + Duration.FromDays(2)), CancellationToken.None);
        await documents.CreateIfAbsentAsync(Sent(from - Duration.FromDays(1)), CancellationToken.None);

        // Act
        var count = await documents.CountFirstSentBetweenAsync(from, from + Duration.FromDays(30), CancellationToken.None);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task FindWaitingOnAsync_DraftThatWaits_IsFound()
    {
        // Arrange
        var preceding = Guid.NewGuid();
        var follower = TestData.Document(precededBy: preceding);

        await documents.CreateIfAbsentAsync(follower, CancellationToken.None);

        // Act
        var found = await documents.FindWaitingOnAsync(preceding, CancellationToken.None);

        // Assert
        Assert.Equal(follower.Id, Assert.Single(found).Id);
    }

    [Fact]
    public async Task FindByProviderDocumentIdAsync_SentDocument_IsFound()
    {
        // Arrange
        var document = Sent(TestData.Now);

        await documents.CreateIfAbsentAsync(document, CancellationToken.None);

        // Act
        var found = await documents.FindByProviderDocumentIdAsync("provider-1", CancellationToken.None);

        // Assert
        Assert.Equal(document.Id, found!.Id);
    }

    [Fact]
    public async Task ReserveAsync_TwoDocumentsAtOnce_NeverGetTheSameNumber()
    {
        // Arrange
        var ranges = provider.GetRequiredService<INumberingRangeRepository>();
        var issuer = TestData.EnabledIssuer(tenant: Guid.NewGuid());
        var range = TestData.Range(issuer, from: 1, to: 1000);
        var numbering = new NumberingService(ranges);
        var today = new LocalDate(2026, 11, 4);

        await ranges.CreateIfAbsentAsync(range, CancellationToken.None);

        // Act
        var numbers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => numbering.ReserveAsync(range.Id, today, CancellationToken.None)));

        // Assert
        Assert.Equal(8, numbers.Select(x => x.Consecutive).Distinct().Count());
    }

    [Fact]
    public async Task UpdateWithRetryAsync_TwoReservationsOfQuotaAtOnce_CountsBoth()
    {
        // Arrange
        var quotas = provider.GetRequiredService<IDocumentQuotaRepository>();
        var tenant = Guid.NewGuid();
        var quota = DocumentQuotaAggregate.CreateEmpty(tenant, TestData.SystemUser);

        quota.ApplyAllowance(100, TestData.Now + Duration.FromDays(300), TestData.Now, TestData.Now, TestData.SystemUser);
        await quotas.CreateAsync(quota, CancellationToken.None);

        Task Reserve() => quotas.UpdateWithRetryAsync(
            () => quotas.FindAsync<DocumentQuotaAggregate>(quota.Id, CancellationToken.None),
            fresh =>
            {
                fresh.Reserve(TestData.Now, true, [], TestData.SystemUser);

                return true;
            },
            CancellationToken.None);

        // Act
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Reserve()));

        // Assert
        Assert.Equal(6, (await quotas.FindAsync<DocumentQuotaAggregate>(quota.Id, CancellationToken.None)).Used);
    }

    [Fact]
    public async Task FindAsync_SavedIssuer_ReadsBackItsMandateAndAddress()
    {
        // Arrange
        var issuers = provider.GetRequiredService<IIssuerRepository>();
        var issuer = TestData.EnabledIssuer(tenant: Guid.NewGuid());

        await issuers.CreateIfAbsentAsync(issuer, CancellationToken.None);

        // Act
        var stored = await issuers.FindAsync<IssuerAggregate>(issuer.Id, CancellationToken.None);

        // Assert
        Assert.Equal(("1.0", "11001", IssuerStatus.Enabled, true), (stored.Mandate!.Version, stored.Address.CityCode, stored.Status, stored.CanIssue));
    }
}
