namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class DocumentQuotaAggregateTest
{
    private static readonly Instant Now = TestData.Now;

    private static readonly Instant LicenseEnd = Now + Duration.FromDays(300);

    private static readonly int[] Alerts = [80, 100];

    private static DocumentQuotaAggregate Quota(long limit = 10, Instant? until = null)
    {
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.Tenant, TestData.SystemUser);

        quota.ApplyAllowance(limit, until ?? LicenseEnd, Now, Now, TestData.SystemUser);

        return quota;
    }

    private static string CodeOf(Action action) => Assert.Throws<CodeDesignPlusException>(action).Code;

    [Fact]
    public void ApplyAllowance_First_StartsAPeriodUntilTheEndOfTheLicense()
    {
        // Act
        var quota = Quota();

        // Assert
        Assert.Equal((10L, (Instant?)Now, (Instant?)LicenseEnd, 10L), (quota.Limit, quota.PeriodStart, quota.PeriodEnd, quota.RemainingAt(Now)));
    }

    [Fact]
    public void Reserve_InvoiceWithoutRoom_ThrowsQuotaExhausted()
    {
        // Arrange
        var quota = Quota(1);

        quota.Reserve(Now, true, Alerts, TestData.SystemUser);

        // Act & Assert
        Assert.Equal(Errors.QuotaExhausted.GetCode(), CodeOf(() => quota.Reserve(Now, true, Alerts, TestData.SystemUser)));
    }

    [Fact]
    public void Reserve_NoteWithoutRoom_GoesAndIsCounted()
    {
        // Arrange
        var quota = Quota(1);

        quota.Reserve(Now, true, Alerts, TestData.SystemUser);

        // Act
        quota.Reserve(Now, false, Alerts, TestData.SystemUser);

        // Assert
        Assert.Equal((2L, 0L), (quota.Used, quota.RemainingAt(Now)));
    }

    [Fact]
    public void Reserve_WithoutAllowance_ThrowsQuotaExhausted()
    {
        // Arrange
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.Tenant, TestData.SystemUser);

        // Act & Assert
        Assert.Equal(Errors.QuotaExhausted.GetCode(), CodeOf(() => quota.Reserve(Now, true, Alerts, TestData.SystemUser)));
    }

    [Fact]
    public void Reserve_AfterTheLicenseEnds_ThrowsQuotaExhausted()
    {
        // Arrange
        var quota = Quota();

        // Act & Assert
        Assert.Equal(Errors.QuotaExhausted.GetCode(), CodeOf(() => quota.Reserve(LicenseEnd, true, Alerts, TestData.SystemUser)));
    }

    [Fact]
    public void Reserve_CrossingThresholds_AnnouncesEachOnce()
    {
        // Arrange
        var quota = Quota(10);

        // Act
        for (var i = 0; i < 10; i++)
        {
            quota.Reserve(Now, true, Alerts, TestData.SystemUser);
        }

        // Assert
        var announced = quota.GetAndClearEvents().OfType<DocumentQuotaThresholdReachedDomainEvent>().Select(x => (x.Percentage, x.Used)).ToList();

        Assert.Equal([(80, 8L), (100, 10L)], announced);
    }

    [Fact]
    public void ApplyAllowance_LargerLimitInTheSamePeriod_KeepsTheCountAndAnnouncesAgainBelowTheNewLimit()
    {
        // Arrange: 8 of 10 used, the 80 % announced; the package is extended to 20.
        var quota = Quota(10);

        for (var i = 0; i < 8; i++)
        {
            quota.Reserve(Now, true, Alerts, TestData.SystemUser);
        }

        // Act
        quota.ApplyAllowance(20, LicenseEnd, Now + Duration.FromMinutes(1), Now, TestData.SystemUser);

        // Assert
        Assert.Equal((20L, 8L, 0), (quota.Limit, quota.Used, quota.AnnouncedPercentages.Count));
    }

    [Fact]
    public void ApplyAllowance_RenewedBeforeTheEnd_StartsTheNewPeriodWhereTheCurrentEnds()
    {
        // Arrange
        var quota = Quota(10);
        var renewedUntil = LicenseEnd + Duration.FromDays(365);

        quota.Reserve(Now, true, Alerts, TestData.SystemUser);

        // Act
        quota.ApplyAllowance(10, renewedUntil, Now + Duration.FromMinutes(1), Now, TestData.SystemUser);

        // Assert: until the current period ends, the count goes on; after, it starts again.
        Assert.Equal((9L, 10L, (Instant?)renewedUntil), (quota.RemainingAt(Now), quota.RemainingAt(LicenseEnd), quota.PeriodEndAt(LicenseEnd)));
    }

    [Fact]
    public void Reserve_InTheRenewedPeriod_CountsFromZero()
    {
        // Arrange
        var quota = Quota(10);
        var renewedUntil = LicenseEnd + Duration.FromDays(365);

        quota.Reserve(Now, true, Alerts, TestData.SystemUser);
        quota.ApplyAllowance(10, renewedUntil, Now + Duration.FromMinutes(1), Now, TestData.SystemUser);

        // Act
        quota.Reserve(LicenseEnd + Duration.FromDays(1), true, Alerts, TestData.SystemUser);

        // Assert
        Assert.Equal((1L, (Instant?)LicenseEnd, (Instant?)renewedUntil), (quota.Used, quota.PeriodStart, quota.PeriodEnd));
    }

    [Fact]
    public void ApplyAllowance_OlderThanTheApplied_IsIgnored()
    {
        // Arrange
        var quota = Quota(10);

        quota.ApplyAllowance(20, LicenseEnd, Now + Duration.FromMinutes(5), Now, TestData.SystemUser);

        // Act
        var applied = quota.ApplyAllowance(5, LicenseEnd, Now + Duration.FromMinutes(1), Now, TestData.SystemUser);

        // Assert
        Assert.Equal((false, 20L), (applied, quota.Limit));
    }

    [Fact]
    public void ApplyAllowance_LicenseAlreadyEnded_LeavesNoRoom()
    {
        // Act
        var quota = Quota(10, Now - Duration.FromDays(1));

        // Assert
        Assert.Equal(0L, quota.RemainingAt(Now));
    }

    [Fact]
    public void ApplyPlatformAllowance_NewContract_StartsTheCountAgain()
    {
        // Arrange
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.PlatformTenant, TestData.SystemUser);
        var from = Now - Duration.FromDays(10);
        var until = from + Duration.FromDays(365);

        quota.ApplyPlatformAllowance(500, from, until, Now, TestData.SystemUser);
        quota.Reserve(Now, true, Alerts, TestData.SystemUser);

        // Act
        quota.ApplyPlatformAllowance(500, until, until + Duration.FromDays(365), until + Duration.FromDays(1), TestData.SystemUser);

        // Assert
        Assert.Equal((500L, 0L), (quota.Limit, quota.Used));
    }

    [Fact]
    public void ApplyPlatformAllowance_SameContract_KeepsTheCount()
    {
        // Arrange
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.PlatformTenant, TestData.SystemUser);
        var from = Now - Duration.FromDays(10);

        quota.ApplyPlatformAllowance(500, from, from + Duration.FromDays(365), Now, TestData.SystemUser);
        quota.Reserve(Now, true, Alerts, TestData.SystemUser);

        // Act
        quota.ApplyPlatformAllowance(500, from, from + Duration.FromDays(366), Now, TestData.SystemUser);

        // Assert
        Assert.Equal(1L, quota.Used);
    }
}

public class ProviderPlanUsageAggregateTest
{
    private static readonly LocalDate November = new(2026, 11, 1);

    private static readonly Instant MonthStart = Instant.FromUtc(2026, 11, 1, 5, 0);

    private static readonly Instant MonthEnd = Instant.FromUtc(2026, 12, 1, 5, 0);

    private static readonly Instant ContractEnd = Instant.FromUtc(2027, 11, 4, 5, 0);

    private static readonly OverageTier[] Tiers = [new(200_000, 1_400), new(500_000, 1_000), new(1_000_000, 600), new(null, 400)];

    private static readonly int[] Alerts = [70, 90, 100];

    private static ProviderPlanUsageAggregate Usage() => ProviderPlanUsageAggregate.Start("Alanube", November, MonthStart, MonthEnd, "COP", TestData.SystemUser);

    private static void Record(ProviderPlanUsageAggregate usage, long used, Instant? now = null) => usage.Record(used, 3_000, Tiers, ContractEnd, Alerts, Duration.FromDays(30), now ?? TestData.Now, TestData.SystemUser);

    [Fact]
    public void Record_CrossingThresholds_AnnouncesEachOnceInTheMonth()
    {
        // Arrange
        var usage = Usage();

        // Act
        Record(usage, 2_100);
        Record(usage, 2_750);
        Record(usage, 3_000);
        Record(usage, 3_200);

        // Assert
        Assert.Equal([70, 90, 100], usage.GetAndClearEvents().OfType<ProviderPlanThresholdReachedDomainEvent>().Select(x => x.Percentage).ToList());
    }

    [Fact]
    public void Record_OverTheIncludedDocuments_KeepsTheOverageAndItsCostWithoutBlocking()
    {
        // Arrange
        var usage = Usage();

        // Act: 500 documents over the 3,000 included, at COP 14 each.
        Record(usage, 3_500);

        // Assert
        Assert.Equal((3_500L, 500L, 700_000L, "COP"), (usage.Used, usage.Overage, usage.EstimatedOverageCost, usage.Currency));
    }

    [Fact]
    public void Record_ThresholdOfTheIncludedDocuments_CarriesTheOverageOfTheMonth()
    {
        // Arrange
        var usage = Usage();

        // Act
        Record(usage, 3_010);

        // Assert
        var reached = usage.GetAndClearEvents().OfType<ProviderPlanThresholdReachedDomainEvent>().Single(x => x.Percentage == 100);

        Assert.Equal((November, 10L, 14_000L, "COP", MonthEnd), (reached.Month, reached.Overage, reached.EstimatedOverageCost, reached.Currency, reached.PeriodEnd));
    }

    [Fact]
    public void Record_UnderTheIncludedDocuments_HasNoOverage()
    {
        // Arrange
        var usage = Usage();

        // Act
        Record(usage, 2_999);

        // Assert
        Assert.Equal((0L, 0L), (usage.Overage, usage.EstimatedOverageCost));
    }

    [Fact]
    public void Record_NoticeOfTheEndOpensInTheMonth_AnnouncesItOnce()
    {
        // Arrange
        var usage = ProviderPlanUsageAggregate.Start("Alanube", new LocalDate(2027, 10, 1), Instant.FromUtc(2027, 10, 1, 5, 0), Instant.FromUtc(2027, 11, 1, 5, 0), "COP", TestData.SystemUser);
        var late = ContractEnd - Duration.FromDays(29);

        // Act
        Record(usage, 1, late);
        Record(usage, 2, late);

        // Assert
        Assert.Single(usage.GetAndClearEvents().OfType<ProviderPlanExpiringDomainEvent>());
    }

    [Fact]
    public void Record_NoticeOfTheEndOpenedInAnEarlierMonth_IsNotAnnouncedAgain()
    {
        // Arrange: the notice opened on 5 October; November's usage does not repeat it.
        var usage = ProviderPlanUsageAggregate.Start("Alanube", new LocalDate(2027, 11, 1), Instant.FromUtc(2027, 11, 1, 5, 0), ContractEnd, "COP", TestData.SystemUser);

        // Act
        Record(usage, 1, ContractEnd - Duration.FromDays(1));

        // Assert
        Assert.Empty(usage.GetAndClearEvents().OfType<ProviderPlanExpiringDomainEvent>());
    }

    [Fact]
    public void Start_DayThatIsNotTheFirstOfTheMonth_ThrowsQuotaPeriodIsInvalid()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ProviderPlanUsageAggregate.Start("Alanube", new LocalDate(2026, 11, 2), MonthStart, MonthEnd, "COP", TestData.SystemUser));

        // Assert
        Assert.Equal(Errors.QuotaPeriodIsInvalid.GetCode(), exception.Code);
    }

    [Fact]
    public void IdFor_SameMonth_IsTheSame()
    {
        // Act & Assert
        Assert.Equal(ProviderPlanUsageAggregate.IdFor("Alanube", MonthStart), Usage().Id);
    }

    [Fact]
    public void IdFor_AnotherMonth_IsAnother()
    {
        // Act & Assert: the included documents do not carry over, so each month has its own usage.
        Assert.NotEqual(ProviderPlanUsageAggregate.IdFor("Alanube", MonthEnd), Usage().Id);
    }
}

public class OverageTierTest
{
    private static readonly OverageTier[] Tiers = [new(200_000, 1_400), new(500_000, 1_000), new(1_000_000, 600), new(null, 400)];

    [Theory]
    [InlineData(3_000, 0)]
    [InlineData(3_001, 1_400)]
    [InlineData(200_000, 197_000L * 1_400)]
    [InlineData(200_001, 197_000L * 1_400 + 1_000)]
    [InlineData(500_000, 197_000L * 1_400 + 300_000L * 1_000)]
    [InlineData(1_000_001, 197_000L * 1_400 + 300_000L * 1_000 + 500_000L * 600 + 400)]
    public void CostOf_DocumentsOfTheMonth_PricesEachByTheTierItsPositionFallsIn(long used, long expected)
    {
        // Act
        var cost = OverageTier.CostOf(used, 3_000, Tiers);

        // Assert
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void CostOf_WithoutTiers_IsZero()
    {
        // Act & Assert
        Assert.Equal(0, OverageTier.CostOf(10_000, 3_000, []));
    }
}
