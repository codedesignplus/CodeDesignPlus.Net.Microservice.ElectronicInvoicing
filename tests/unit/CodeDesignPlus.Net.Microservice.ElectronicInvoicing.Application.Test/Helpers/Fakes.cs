namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

/// <summary>
/// What the handlers share in the tests: options, a fixed clock and repositories whose optimistic update runs the change
/// on what <c>FindAsync</c> returns, as the SDK does.
/// </summary>
public static class Fakes
{
    public static ElectronicInvoicingOptions Options() => new()
    {
        Routing = new() { ["CO"] = ["Simulated", "Other"] },
        SystemUser = TestData.SystemUser,
        SecretsKeyName = "electronic-issuers",
        FilesTarget = "electronic-documents",
        PlatformIssuer = new PlatformIssuerOptions { ReservedDocuments = 500, FilesTarget = "platform-electronic-documents" },
        ProviderPlan = new ProviderPlanOptions { Provider = "Simulated", Documents = 40_000, ValidFrom = new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), ValidUntil = new DateTimeOffset(2027, 11, 1, 5, 0, 0, TimeSpan.Zero), AlertPercentages = [70, 90] },
        Quota = new QuotaOptions { AttributeKeys = ["documents_key"], AlertPercentages = [80, 100] },
        StatusCheck = new StatusCheckOptions { InitialDelay = TimeSpan.FromMinutes(1), MaxDelay = TimeSpan.FromMinutes(30), BackoffFactor = 2, MaxAttempts = 5, SentTimeout = TimeSpan.FromHours(6), BatchSize = 100, Lease = TimeSpan.FromMinutes(3) }
    };

    public static IOptions<ElectronicInvoicingOptions> Options(ElectronicInvoicingOptions options) => Microsoft.Extensions.Options.Options.Create(options);

    public static IClock Clock(Instant? now = null) => new FixedClock(now ?? TestData.Now);

    public static ProviderCapabilities Capabilities(NumberingMode numbering = NumberingMode.ByPlatform, CredentialScope scope = CredentialScope.MasterAccount, bool externalReference = true, StatusUpdatesMode updates = StatusUpdatesMode.Poll) => new(
        IssuerProvisioningMode.Api,
        scope,
        numbering,
        true,
        new HashSet<ElectronicDocumentType>(Enum.GetValues<ElectronicDocumentType>()),
        true,
        externalReference,
        updates,
        IdempotencyMode.LookupByNumber,
        true,
        true,
        false);

    /// <summary>
    /// Makes <c>UpdateWithRetryAsync</c> load and change like the SDK, on whatever <c>FindAsync</c> returns.
    /// </summary>
    public static void EmulateOptimisticUpdate<TRepository, TEntity>(this Mock<TRepository> repository)
        where TRepository : class, IRepositoryBase
        where TEntity : class, IEntityBase, IVersionedAggregate
    {
        repository
            .Setup(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<TEntity>>>(), It.IsAny<Func<TEntity, bool>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<TEntity>>, Func<TEntity, bool>, CancellationToken>(async (load, change, _) =>
            {
                var entity = await load();

                return entity is not null && change(entity);
            });
    }

    /// <summary>
    /// The events published, flattened.
    /// </summary>
    public static List<IDomainEvent> Published(this Mock<IPubSub> pubsub) => [.. pubsub.Invocations
        .Where(x => x.Method.Name == nameof(IPubSub.PublishAsync))
        .SelectMany(x => x.Arguments[0] switch
        {
            IEnumerable<IDomainEvent> many => many,
            IDomainEvent one => [one],
            _ => []
        })];

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
