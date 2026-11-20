namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// How a quota says it stands at an instant: what the event of the availability carries.
/// </summary>
public class QuotaStandingTest
{
    private static readonly Instant Now = TestData.Now;

    private static readonly Instant LicenseEnd = Now + Duration.FromDays(300);

    private static readonly Instant RenewedUntil = LicenseEnd + Duration.FromDays(365);

    private static DocumentQuotaAggregate Quota(long limit, long used = 0)
    {
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.Tenant, TestData.SystemUser);

        quota.ApplyAllowance(limit, LicenseEnd, Now, Now, TestData.SystemUser);

        for (var i = 0; i < used; i++)
        {
            quota.Reserve(Now, false, [], TestData.SystemUser);
        }

        return quota;
    }

    [Fact]
    public void StandingAt_QuotaWithoutAllowance_AllowsNothing()
    {
        // Act
        var quota = DocumentQuotaAggregate.CreateEmpty(TestData.Tenant, TestData.SystemUser);

        // Assert
        Assert.Equal((QuotaStanding.None, false), (quota.StandingAt(Now), quota.HasRoomAt(Now)));
    }

    [Fact]
    public void StandingAt_QuotaWithRoom_SaysWhatIsLeftInItsPeriod()
    {
        // Act
        var quota = Quota(10, 4);

        // Assert
        Assert.Equal((new QuotaStanding(10, 4, 6, Now, LicenseEnd, null), true), (quota.StandingAt(Now), quota.HasRoomAt(Now)));
    }

    [Fact]
    public void StandingAt_QuotaUsedPastItsLimit_HasNothingLeftAndSaysTheRealCount()
    {
        // Act: the notes took the count past the limit.
        var quota = Quota(10, 13);

        // Assert
        Assert.Equal((10L, 13L, 0L, false), (quota.StandingAt(Now).Limit, quota.StandingAt(Now).Used, quota.StandingAt(Now).Remaining, quota.HasRoomAt(Now)));
    }

    [Fact]
    public void StandingAt_AfterThePeriodEnded_HasNothingLeft()
    {
        // Arrange
        var quota = Quota(10, 4);

        // Act
        var standing = quota.StandingAt(LicenseEnd);

        // Assert
        Assert.Equal((0L, false, (Instant?)LicenseEnd), (standing.Remaining, quota.HasRoomAt(LicenseEnd), standing.PeriodEnd));
    }

    [Fact]
    public void StandingAt_RenewedBeforeThePeriodEnds_SaysTheCurrentPeriodAndUntilWhenItWasRenewed()
    {
        // Arrange
        var quota = Quota(10, 4);

        quota.ApplyAllowance(10, RenewedUntil, Now, Now, TestData.SystemUser);

        // Act
        var standing = quota.StandingAt(Now);

        // Assert
        Assert.Equal(new QuotaStanding(10, 4, 6, Now, LicenseEnd, RenewedUntil), standing);
    }

    [Fact]
    public void StandingAt_RenewedOnceThePeriodEnded_IsTheNewPeriodFromZero()
    {
        // Arrange: nothing rolled the period yet; the standing already reads the renewed one.
        var quota = Quota(10, 10);

        quota.ApplyAllowance(10, RenewedUntil, Now, Now, TestData.SystemUser);

        // Act
        var standing = quota.StandingAt(LicenseEnd + Duration.FromSeconds(1));

        // Assert
        Assert.Equal((new QuotaStanding(10, 0, 10, LicenseEnd, RenewedUntil, null), true), (standing, quota.HasRoomAt(LicenseEnd + Duration.FromSeconds(1))));
    }
}
