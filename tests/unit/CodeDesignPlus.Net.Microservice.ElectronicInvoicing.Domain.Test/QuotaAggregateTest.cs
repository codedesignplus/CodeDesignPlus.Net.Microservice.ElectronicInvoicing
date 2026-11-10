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
    private static readonly Instant From = TestData.Now - Duration.FromDays(100);

    private static readonly Instant Until = From + Duration.FromDays(365);

    private static ProviderPlanUsageAggregate Usage() => ProviderPlanUsageAggregate.Start("Aliaddo", 40_000, From, Until, TestData.SystemUser);

    [Fact]
    public void Record_CrossingThresholds_AnnouncesEachOnce()
    {
        // Arrange
        var usage = Usage();

        // Act
        usage.Record(28_000, 40_000, Until, [70, 90], Duration.FromDays(30), TestData.Now, TestData.SystemUser);
        usage.Record(36_500, 40_000, Until, [70, 90], Duration.FromDays(30), TestData.Now, TestData.SystemUser);
        usage.Record(37_000, 40_000, Until, [70, 90], Duration.FromDays(30), TestData.Now, TestData.SystemUser);

        // Assert
        Assert.Equal([70, 90], usage.GetAndClearEvents().OfType<ProviderPlanThresholdReachedDomainEvent>().Select(x => x.Percentage).ToList());
    }

    [Fact]
    public void Record_CloseToTheEndOfTheContract_AnnouncesItOnce()
    {
        // Arrange
        var usage = Usage();
        var late = Until - Duration.FromDays(10);

        // Act
        usage.Record(1, 40_000, Until, [70, 90], Duration.FromDays(30), late, TestData.SystemUser);
        usage.Record(2, 40_000, Until, [70, 90], Duration.FromDays(30), late, TestData.SystemUser);

        // Assert
        Assert.Single(usage.GetAndClearEvents().OfType<ProviderPlanExpiringDomainEvent>());
    }

    [Fact]
    public void IdFor_SamePlan_IsTheSame()
    {
        // Act & Assert
        Assert.Equal(ProviderPlanUsageAggregate.IdFor("Aliaddo", From), Usage().Id);
    }
}
