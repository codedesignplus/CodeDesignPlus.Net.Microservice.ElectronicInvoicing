namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class TestProviderGrantAggregateTest
{
    private static readonly Guid Operator = Guid.Parse("aa000000-0000-4000-8000-0000000000a1");

    private static readonly Instant Later = TestData.Now + Duration.FromHours(2);

    [Fact]
    public void Grant_NewTenant_IsGrantedAndAnnouncesIt()
    {
        // Act
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, Operator, TestData.Now);

        // Assert
        var granted = Assert.IsType<TestProviderGrantedDomainEvent>(Assert.Single(grant.GetAndClearEvents()));

        Assert.Equal((TestProviderGrantAggregate.IdFor(TestData.Tenant), true, Operator, TestData.Now, TestData.Tenant, Operator), (grant.Id, grant.Granted, grant.GrantedBy, grant.GrantedAt, granted.Tenant, granted.GrantedBy));
    }

    [Fact]
    public void Grant_TenantReservedForThePlatform_ThrowsTestProviderNotForThePlatform()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => TestProviderGrantAggregate.Grant(PlatformScope.Tenant, Operator, TestData.Now));

        // Assert
        Assert.Equal(Errors.TestProviderNotForThePlatform.GetCode(), exception.Code);
    }

    [Fact]
    public void Grant_WithoutTenant_ThrowsTenantIsRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => TestProviderGrantAggregate.Grant(Guid.Empty, Operator, TestData.Now));

        // Assert
        Assert.Equal(Errors.TenantIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Revoke_Granted_KeepsWhoAndWhenAndAnnouncesIt()
    {
        // Arrange
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, Operator, TestData.Now);

        grant.GetAndClearEvents();

        // Act
        var revoked = grant.Revoke(TestData.User, Later);

        // Assert
        var announced = Assert.IsType<TestProviderRevokedDomainEvent>(Assert.Single(grant.GetAndClearEvents()));

        Assert.Equal((true, false, (Guid?)TestData.User, (Instant?)Later, Operator, TestData.Tenant), (revoked, grant.Granted, grant.RevokedBy, grant.RevokedAt, grant.GrantedBy, announced.Tenant));
    }

    [Fact]
    public void Revoke_AlreadyRevoked_ChangesNothing()
    {
        // Arrange
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, Operator, TestData.Now);

        grant.Revoke(TestData.User, Later);
        grant.GetAndClearEvents();

        // Act
        var revoked = grant.Revoke(Operator, Later + Duration.FromHours(1));

        // Assert
        Assert.Equal((false, (Guid?)TestData.User, (Instant?)Later, 0), (revoked, grant.RevokedBy, grant.RevokedAt, grant.GetAndClearEvents().Count));
    }

    [Fact]
    public void GrantAgain_Revoked_GrantsItForgetsTheRevocationAndAnnouncesIt()
    {
        // Arrange
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, Operator, TestData.Now);

        grant.Revoke(TestData.User, Later);
        grant.GetAndClearEvents();

        // Act
        var granted = grant.GrantAgain(TestData.User, Later + Duration.FromHours(1));

        // Assert
        Assert.IsType<TestProviderGrantedDomainEvent>(Assert.Single(grant.GetAndClearEvents()));

        Assert.Equal((true, true, TestData.User, Later + Duration.FromHours(1), (Guid?)null, (Instant?)null), (granted, grant.Granted, grant.GrantedBy, grant.GrantedAt, grant.RevokedBy, grant.RevokedAt));
    }

    [Fact]
    public void GrantAgain_AlreadyGranted_ChangesNothing()
    {
        // Arrange
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, Operator, TestData.Now);

        grant.GetAndClearEvents();

        // Act
        var granted = grant.GrantAgain(TestData.User, Later);

        // Assert
        Assert.Equal((false, Operator, TestData.Now, 0), (granted, grant.GrantedBy, grant.GrantedAt, grant.GetAndClearEvents().Count));
    }
}
