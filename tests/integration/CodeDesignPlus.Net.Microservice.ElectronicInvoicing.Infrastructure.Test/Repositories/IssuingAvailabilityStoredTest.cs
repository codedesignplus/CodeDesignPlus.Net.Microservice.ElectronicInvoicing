using CodeDesignPlus.Net.Core.Abstractions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using CodeDesignPlus.Net.PubSub.Abstractions;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Repositories;

/// <summary>
/// The state of an issuer as it is announced, with the quota and the issuer read from a real Mongo: the last unit, a unit
/// given back, an allowance that shrinks below what is used and one that grows.
/// </summary>
[Collection(MongoContainerFixture.Collection)]
public class IssuingAvailabilityStoredTest
{
    private static readonly Instant Now = TestData.Now;

    private static readonly Instant LicenseEnd = Now + Duration.FromDays(300);

    private readonly Guid tenant = Guid.NewGuid();

    private readonly Bus bus = new();

    private readonly IIssuerRepository issuers;

    private readonly QuotaGate gate;

    private readonly ApplyLicenseQuotaCommandHandler licenses;

    public IssuingAvailabilityStoredTest(MongoContainerFixture fixture)
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

        var provider = services.BuildServiceProvider();
        var settings = Microsoft.Extensions.Options.Options.Create(new ElectronicInvoicingOptions
        {
            Routing = new() { ["CO"] = ["Simulated"] },
            SystemUser = TestData.SystemUser,
            Quota = new QuotaOptions { AttributeKeys = ["documents_key"], AlertPercentages = [80, 100] }
        });
        var quotas = provider.GetRequiredService<IDocumentQuotaRepository>();
        var clock = new FixedClock(Now);

        issuers = provider.GetRequiredService<IIssuerRepository>();

        var announcer = new IssuingAvailabilityAnnouncer(issuers, quotas, provider.GetRequiredService<ITestProviderGrantRepository>(), bus, clock, settings);

        gate = new QuotaGate(quotas, announcer, settings);
        licenses = new ApplyLicenseQuotaCommandHandler(quotas, announcer, bus, clock, settings);
    }

    private Task AllowAsync(long documents, int minute) => licenses.Handle(new ApplyLicenseQuotaCommand(tenant, new Dictionary<string, string> { ["documents_key"] = documents.ToString(System.Globalization.CultureInfo.InvariantCulture) }, LicenseEnd, Now + Duration.FromMinutes(minute)), CancellationToken.None);

    private async Task<IReadOnlyList<IDomainEvent>> IssueAsync(ElectronicDocumentType type = ElectronicDocumentType.Invoice)
    {
        var events = await gate.ReserveAsync(tenant, type, Now, CancellationToken.None);

        await bus.PublishAsync(events, CancellationToken.None);

        return events;
    }

    private IssuingAvailabilityChangedDomainEvent Last() => bus.Published.OfType<IssuingAvailabilityChangedDomainEvent>().Last();

    private int Count() => bus.Published.OfType<IssuingAvailabilityChangedDomainEvent>().Count();

    [Fact]
    public async Task Quota_FromTheFirstAllowanceToItsLastUnitAndBack_AnnouncesEachChangeOfTheAnswerAndNothingElse()
    {
        // Arrange: an enabled issuer and an allowance of three documents.
        await issuers.CreateIfAbsentAsync(TestData.EnabledIssuer(tenant: tenant), CancellationToken.None);
        await AllowAsync(3, 0);

        var afterAllowance = (Count(), Last().CanIssue, Last().Remaining);

        // Act: two documents leave room, the third uses it up, a note counts past the limit, one unit comes back.
        await IssueAsync();
        await IssueAsync(ElectronicDocumentType.PosTicket);

        var withRoom = Count();

        await IssueAsync();

        var usedUp = (Count(), Last().CanIssue, Last().ReasonCode, Last().Used, Last().Remaining);

        await IssueAsync(ElectronicDocumentType.CreditNote);

        var afterTheNote = Count();

        await gate.ReleaseAsync(tenant, Now, CancellationToken.None);

        var overTheLimit = Count();

        await gate.ReleaseAsync(tenant, Now, CancellationToken.None);

        var roomAgain = (Count(), Last().CanIssue, Last().Used, Last().Remaining);

        // Assert
        Assert.Equal(((1, true, 3L), 1, (2, false, "109", 3L, 0L), 2, 2, (3, true, 2L, 1L)), (afterAllowance, withRoom, usedUp, afterTheNote, overTheLimit, roomAgain));
    }

    [Fact]
    public async Task Quota_AllowanceThatShrinksBelowWhatIsUsedAndGrowsAgain_AnnouncesTheRealRoom()
    {
        // Arrange: three of ten used.
        await issuers.CreateIfAbsentAsync(TestData.EnabledIssuer(tenant: tenant), CancellationToken.None);
        await AllowAsync(10, 0);
        await IssueAsync();
        await IssueAsync();
        await IssueAsync();

        // Act
        await AllowAsync(2, 1);

        var shrunk = (Last().CanIssue, Last().Limit, Last().Used, Last().Remaining);

        await AllowAsync(2, 2);

        var repeated = Count();

        await AllowAsync(8, 3);

        var grown = (Last().CanIssue, Last().Limit, Last().Used, Last().Remaining, Last().PeriodEnd);

        // Assert
        Assert.Equal(((false, 2L, 3L, 0L), 2, (true, 8L, 3L, 5L, (Instant?)LicenseEnd)), (shrunk, repeated, grown));
    }

    [Fact]
    public async Task Quota_TenantWithoutIssuer_AnnouncesThatItCannotIssueWhateverItsRoom()
    {
        // Act: the allowance arrives before the issuer is registered.
        await AllowAsync(5, 0);

        // Assert
        Assert.Equal((false, false, "202", true, 5L, IssuerAggregate.IdFor(tenant)), (Last().CanIssue, Last().IssuerRegistered, Last().ReasonCode, Last().HasRoom, Last().Remaining, Last().AggregateId));
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }

    private sealed class Bus : IPubSub
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
        {
            Published.Add(@event);

            return Task.CompletedTask;
        }

        public Task PublishAsync(IReadOnlyList<IDomainEvent> @event, CancellationToken cancellationToken)
        {
            Published.AddRange(@event);

            return Task.CompletedTask;
        }
    }
}
