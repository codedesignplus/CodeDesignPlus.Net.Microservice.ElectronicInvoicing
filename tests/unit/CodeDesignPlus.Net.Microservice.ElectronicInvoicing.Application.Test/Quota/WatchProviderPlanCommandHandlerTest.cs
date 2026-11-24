using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Quota;

public class ProviderPlanMeterTest
{
    private static readonly Instant NovemberStart = Instant.FromUtc(2026, 11, 1, 5, 0);

    private static readonly Instant DecemberStart = Instant.FromUtc(2026, 12, 1, 5, 0);

    private readonly Mock<IElectronicDocumentRepository> documents = new();

    private readonly Mock<IIssuerRepository> issuers = new();

    private readonly List<Guid> inProduction = [Guid.NewGuid(), Guid.NewGuid()];

    private ProviderPlanMeter Meter(ProviderPlanOptions? plan = null)
    {
        var options = Fakes.Options();

        options.ProviderPlan = plan ?? Fakes.ProviderPlan();

        issuers.Setup(x => x.FindIdsByProviderAsync(options.ProviderPlan.Provider, IssuerEnvironment.Production, It.IsAny<CancellationToken>())).ReturnsAsync(inProduction);

        return new ProviderPlanMeter(documents.Object, issuers.Object, Fakes.Options(options));
    }

    [Fact]
    public async Task MeasureAsync_DayOfTheMonth_CountsTheValidatedDocumentsOfTheIssuersInProductionInTheCalendarMonthOfBogota()
    {
        // Arrange
        documents.Setup(x => x.CountValidatedBetweenAsync(NovemberStart, DecemberStart, inProduction, It.IsAny<CancellationToken>())).ReturnsAsync(42);

        // Act
        var month = await Meter().MeasureAsync(new LocalDate(2026, 11, 17), CancellationToken.None);

        // Assert
        Assert.Equal((new LocalDate(2026, 11, 1), 42L, 0L), (month!.Month, month.Used, month.Overage));
    }

    [Fact]
    public async Task MeasureAsync_MonthOverTheIncludedDocuments_EstimatesTheCostOfTheOverageByTiers()
    {
        // Arrange: 200,010 documents: 197,000 at COP 14 and 10 at COP 10.
        documents.Setup(x => x.CountValidatedBetweenAsync(NovemberStart, DecemberStart, inProduction, It.IsAny<CancellationToken>())).ReturnsAsync(200_010);

        // Act
        var month = await Meter().MeasureAsync(new LocalDate(2026, 11, 1), CancellationToken.None);

        // Assert
        Assert.Equal((3_000L, 197_010L, 197_000L * 1_400 + 10 * 1_000, "COP"), (month!.IncludedDocuments, month.Overage, month.EstimatedOverageCost, month.Currency));
    }

    [Fact]
    public async Task MeasureAsync_MonthBeforeTheContract_IsNull()
    {
        // Act
        var month = await Meter().MeasureAsync(new LocalDate(2026, 10, 1), CancellationToken.None);

        // Assert
        Assert.Null(month);
    }

    [Fact]
    public async Task MeasureAsync_NoIssuerInProduction_CountsNothingWithoutAskingForTheDocuments()
    {
        // Arrange
        inProduction.Clear();

        // Act
        var month = await Meter().MeasureAsync(new LocalDate(2026, 11, 1), CancellationToken.None);

        // Assert
        Assert.Equal(0, month!.Used);

        documents.Verify(x => x.CountValidatedBetweenAsync(It.IsAny<Instant>(), It.IsAny<Instant>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MeasureAsync_ContractThatStartsInTheMonth_CountsFromTheStartOfTheContract()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();
        var signed = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

        plan.ValidFrom = signed;

        documents.Setup(x => x.CountValidatedBetweenAsync(Instant.FromDateTimeOffset(signed), DecemberStart, inProduction, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        // Act
        var month = await Meter(plan).MeasureAsync(new LocalDate(2026, 11, 1), CancellationToken.None);

        // Assert
        Assert.Equal((Instant.FromDateTimeOffset(signed), 3L), (month!.PeriodStart, month.Used));
    }
}

public class WatchProviderPlanCommandHandlerTest
{
    private static readonly Instant NovemberStart = Instant.FromUtc(2026, 11, 1, 5, 0);

    private readonly Mock<IProviderPlanMeter> meter = new();

    private readonly Mock<IProviderPlanUsageRepository> usages = new();

    private readonly Mock<IPubSub> pubsub = new();

    public WatchProviderPlanCommandHandlerTest()
    {
        usages.EmulateOptimisticUpdate<IProviderPlanUsageRepository, ProviderPlanUsageAggregate>();
    }

    private static ProviderPlanMonth Month(LocalDate month, Instant start, Instant end, long used) => new("Simulated", month, start, end, 3_000, used, Math.Max(0, used - 3_000), OverageTier.CostOf(used, 3_000, Fakes.ProviderPlan().Tiers()), "COP");

    private WatchProviderPlanCommandHandler Handler(Instant now) => new(meter.Object, usages.Object, pubsub.Object, Fakes.Clock(now), Fakes.Options(Fakes.Options()));

    [Fact]
    public async Task Handle_CurrentMonth_ReturnsItsDocuments()
    {
        // Arrange
        meter.Setup(x => x.MeasureAsync(new LocalDate(2026, 11, 1), It.IsAny<CancellationToken>())).ReturnsAsync(Month(new LocalDate(2026, 11, 1), NovemberStart, Instant.FromUtc(2026, 12, 1, 5, 0), 7));

        // Act
        var used = await Handler(TestData.Now).Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(7, used);
    }

    [Fact]
    public async Task Handle_MonthOverTheIncludedDocuments_AnnouncesTheThresholdsWithTheOverageAndItsCost()
    {
        // Arrange
        meter.Setup(x => x.MeasureAsync(new LocalDate(2026, 11, 1), It.IsAny<CancellationToken>())).ReturnsAsync(Month(new LocalDate(2026, 11, 1), NovemberStart, Instant.FromUtc(2026, 12, 1, 5, 0), 3_100));

        // Act
        await Handler(TestData.Now).Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        var reached = pubsub.Published().OfType<ProviderPlanThresholdReachedDomainEvent>().ToList();

        Assert.Equal(("70,90,100", 100L, 140_000L), (string.Join(",", reached.Select(x => x.Percentage)), reached[^1].Overage, reached[^1].EstimatedOverageCost));
    }

    [Fact]
    public async Task Handle_LateNightInBogotaOnTheLastDay_CountsTheMonthOfBogotaAndNotTheOneOfUtc()
    {
        // Arrange: 22:00 of 30 November in Bogota is already 1 December in UTC.
        var now = Instant.FromUtc(2026, 12, 1, 3, 0);

        meter.Setup(x => x.MeasureAsync(new LocalDate(2026, 11, 1), It.IsAny<CancellationToken>())).ReturnsAsync(Month(new LocalDate(2026, 11, 1), NovemberStart, Instant.FromUtc(2026, 12, 1, 5, 0), 5));

        // Act
        var used = await Handler(now).Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(5, used);
    }

    [Fact]
    public async Task Handle_PreviousMonthCountedBeforeItsEnd_CountsItAgainToCloseIt()
    {
        // Arrange
        var now = Instant.FromUtc(2026, 12, 1, 6, 0);
        var november = ProviderPlanUsageAggregate.Start("Simulated", new LocalDate(2026, 11, 1), NovemberStart, Instant.FromUtc(2026, 12, 1, 5, 0), "COP", TestData.SystemUser);

        november.Record(2_000, 3_000, [], Instant.FromUtc(2027, 11, 1, 5, 0), [], Duration.Zero, Instant.FromUtc(2026, 12, 1, 4, 0), TestData.SystemUser);

        usages.Setup(x => x.FindAsync<ProviderPlanUsageAggregate>(november.Id, It.IsAny<CancellationToken>())).ReturnsAsync(november);
        meter.Setup(x => x.MeasureAsync(new LocalDate(2026, 11, 1), It.IsAny<CancellationToken>())).ReturnsAsync(Month(new LocalDate(2026, 11, 1), NovemberStart, Instant.FromUtc(2026, 12, 1, 5, 0), 2_050));

        // Act
        await Handler(now).Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        Assert.Equal((2_050L, now), (november.Used, november.CountedAt));
    }

    [Fact]
    public async Task Handle_MonthOutsideTheContract_RecordsNothing()
    {
        // Act
        var used = await Handler(Instant.FromUtc(2028, 1, 10, 12, 0)).Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(0, used);

        usages.Verify(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<ProviderPlanUsageAggregate>>>(), It.IsAny<Func<ProviderPlanUsageAggregate, bool>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class GetProviderPlanMonthQueryHandlerTest
{
    [Fact]
    public async Task Handle_WithoutMonth_MeasuresTheCurrentMonthOfTheTimeZoneOfThePlan()
    {
        // Arrange
        var meter = new Mock<IProviderPlanMeter>();
        var handler = new GetProviderPlanMonthQueryHandler(meter.Object, Fakes.Clock(Instant.FromUtc(2026, 12, 1, 3, 0)), Fakes.Options(Fakes.Options()));

        // Act
        await handler.Handle(new GetProviderPlanMonthQuery(null), CancellationToken.None);

        // Assert
        meter.Verify(x => x.MeasureAsync(new LocalDate(2026, 11, 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithAMonth_MeasuresThatMonth()
    {
        // Arrange
        var meter = new Mock<IProviderPlanMeter>();
        var handler = new GetProviderPlanMonthQueryHandler(meter.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));

        // Act
        await handler.Handle(new GetProviderPlanMonthQuery(new LocalDate(2027, 2, 14)), CancellationToken.None);

        // Assert
        meter.Verify(x => x.MeasureAsync(new LocalDate(2027, 2, 14), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ProviderPlanOptionsTest
{
    [Fact]
    public void Problems_ContractOfAlanube_HasNone()
    {
        // Act & Assert
        Assert.Empty(Fakes.ProviderPlan().Problems());
    }

    [Fact]
    public void Problems_TierThatStartsInsideTheIncludedDocuments_IsAProblem()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.OverageTiers[0].UpTo = 2_000;

        // Act & Assert
        Assert.Contains(plan.Problems(), x => x.StartsWith("ProviderPlan:OverageTiers:0:UpTo", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_TierWithoutEndBeforeTheLast_IsAProblem()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.OverageTiers[1].UpTo = null;

        // Act & Assert
        Assert.Contains(plan.Problems(), x => x.StartsWith("ProviderPlan:OverageTiers:1:UpTo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Mars/Olympus", "TimeZoneId")]
    [InlineData("", "TimeZoneId")]
    public void Problems_UnknownTimeZone_IsAProblem(string zone, string key)
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.TimeZoneId = zone;

        // Act & Assert
        Assert.Contains(plan.Problems(), x => x.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_CurrencyThatIsNotAnIsoCode_IsAProblem()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.Currency = "pesos";

        // Act & Assert
        Assert.Contains(plan.Problems(), x => x.StartsWith("ProviderPlan:Currency", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_WithoutIncludedDocuments_IsAProblem()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.DocumentsPerMonth = 0;

        // Act & Assert
        Assert.Contains(plan.Problems(), x => x.StartsWith("ProviderPlan:DocumentsPerMonth", StringComparison.Ordinal));
    }

    [Fact]
    public void PeriodOf_MonthAfterTheContract_IsNull()
    {
        // Act & Assert
        Assert.Null(Fakes.ProviderPlan().PeriodOf(new LocalDate(2027, 12, 1)));
    }

    [Fact]
    public void PeriodOf_MonthInWhichTheContractEnds_EndsWithTheContract()
    {
        // Arrange
        var plan = Fakes.ProviderPlan();

        plan.ValidUntil = new DateTimeOffset(2027, 11, 15, 5, 0, 0, TimeSpan.Zero);

        // Act
        var period = plan.PeriodOf(new LocalDate(2027, 11, 1));

        // Assert
        Assert.Equal((Instant.FromUtc(2027, 11, 1, 5, 0), Instant.FromUtc(2027, 11, 15, 5, 0)), period);
    }
}
