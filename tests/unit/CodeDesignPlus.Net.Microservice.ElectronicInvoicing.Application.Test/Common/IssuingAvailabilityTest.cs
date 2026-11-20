using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Common;

/// <summary>
/// Quotas and issuers in the states the tests of the availability need.
/// </summary>
public static class Standing
{
    public static readonly Instant LicenseEnd = TestData.Now + Duration.FromDays(300);

    /// <summary>
    /// A quota with an allowance until the end of the license and some documents already counted (as notes, so the count
    /// can go past the limit).
    /// </summary>
    public static DocumentQuotaAggregate Quota(long limit, long used = 0, Guid? tenant = null)
    {
        var quota = DocumentQuotaAggregate.CreateEmpty(tenant ?? TestData.Tenant, TestData.SystemUser);

        quota.ApplyAllowance(limit, LicenseEnd, TestData.Now, TestData.Now, TestData.SystemUser);

        for (var i = 0; i < used; i++)
        {
            quota.Reserve(TestData.Now, false, [], TestData.SystemUser);
        }

        quota.GetAndClearEvents();

        return quota;
    }

    /// <summary>
    /// The quota as it is read once it was saved: a quota never saved (version 0) is always written, whatever changes.
    /// </summary>
    public static DocumentQuotaAggregate AlreadySaved(DocumentQuotaAggregate quota)
    {
        typeof(DocumentQuotaAggregate).GetProperty(nameof(DocumentQuotaAggregate.Version))!.SetValue(quota, 1L);

        return quota;
    }

    /// <summary>
    /// An issuer with its mandate that has not started its tests: it cannot issue yet.
    /// </summary>
    public static IssuerAggregate IssuerWithMandate()
    {
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);
        issuer.GetAndClearEvents();

        return issuer;
    }

    public static IssuerAggregate SuspendedIssuer()
    {
        var issuer = TestData.EnabledIssuer();

        issuer.Suspend("230", TestData.User);
        issuer.GetAndClearEvents();

        return issuer;
    }
}

public class IssuingAvailabilityAnnouncerTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IDocumentQuotaRepository> quotas = new();
    private readonly Mock<ITestProviderGrantRepository> grants = new();
    private readonly Mock<IPubSub> pubsub = new();

    private IssuingAvailabilityAnnouncer Announcer(ElectronicInvoicingOptions? options = null) => new(issuers.Object, quotas.Object, grants.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(options ?? Fakes.Options()));

    private void Store(IssuerAggregate issuer) => issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

    private void Store(DocumentQuotaAggregate quota) => quotas.Setup(x => x.FindAsync<DocumentQuotaAggregate>(quota.Id, It.IsAny<CancellationToken>())).ReturnsAsync(quota);

    [Fact]
    public void Of_EnabledIssuerWithRoom_CanIssueWithoutAReason()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, TestData.EnabledIssuer(), true, Standing.Quota(10, 4).StandingAt(TestData.Now));

        // Assert
        Assert.Equal((true, null, true, true, "Enabled", true, 10L, 4L, 6L, (Instant?)TestData.Now, (Instant?)Standing.LicenseEnd, null), (state.CanIssue, state.ReasonCode, state.IssuerRegistered, state.IssuerCanIssue, state.IssuerStatus, state.HasRoom, state.Limit, state.Used, state.Remaining, state.PeriodStart, state.PeriodEnd, state.RenewedUntil));
    }

    [Fact]
    public void Of_AnyTenant_IsKeyedByTheIdOfItsIssuerEvenBeforeItIsRegistered()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, null, true, QuotaStanding.None);

        // Assert
        Assert.Equal((IssuerAggregate.IdFor(TestData.Tenant), TestData.Tenant), (state.AggregateId, state.Tenant));
    }

    [Fact]
    public void Of_TenantWithoutIssuer_CannotIssueBecauseThereIsNone()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, null, true, Standing.Quota(10).StandingAt(TestData.Now));

        // Assert
        Assert.Equal((false, AppErrors.IssuerNotFound.GetCode(), false, false, null, true), (state.CanIssue, state.ReasonCode, state.IssuerRegistered, state.IssuerCanIssue, state.IssuerStatus, state.HasRoom));
    }

    [Fact]
    public void Of_IssuerWithoutMandate_CannotIssueBecauseOfTheMandate()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, TestData.RegisteredIssuer(), true, Standing.Quota(10).StandingAt(TestData.Now));

        // Assert
        Assert.Equal((false, DomainErrors.MandateNotAccepted.GetCode(), true, "Draft"), (state.CanIssue, state.ReasonCode, state.IssuerRegistered, state.IssuerStatus));
    }

    [Fact]
    public void Of_SuspendedIssuer_CannotIssueBecauseItIsNotEnabled()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, Standing.SuspendedIssuer(), true, Standing.Quota(10).StandingAt(TestData.Now));

        // Assert
        Assert.Equal((false, DomainErrors.IssuerNotEnabled.GetCode(), false, "Suspended", true), (state.CanIssue, state.ReasonCode, state.IssuerCanIssue, state.IssuerStatus, state.HasRoom));
    }

    [Fact]
    public void Of_TestProviderWithoutPermission_CannotIssueBecauseOfThePermission()
    {
        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, TestData.EnabledIssuer(), false, Standing.Quota(10).StandingAt(TestData.Now));

        // Assert
        Assert.Equal((false, AppErrors.TestProviderNotGranted.GetCode(), false), (state.CanIssue, state.ReasonCode, state.IssuerCanIssue));
    }

    [Fact]
    public void Of_EnabledIssuerWithItsQuotaUsedUp_CannotIssueBecauseOfTheQuotaButItsNotesStillGo()
    {
        // Arrange: the notes kept counting after the last unit.
        var quota = Standing.Quota(10, 13);

        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, TestData.EnabledIssuer(), true, quota.StandingAt(TestData.Now));

        // Assert
        Assert.Equal((false, DomainErrors.QuotaExhausted.GetCode(), true, false, 10L, 13L, 0L), (state.CanIssue, state.ReasonCode, state.IssuerCanIssue, state.HasRoom, state.Limit, state.Used, state.Remaining));
    }

    [Fact]
    public void Of_EnabledIssuerWhoseQuotaEndedWithDocumentsLeft_CannotIssueBecauseOfTheQuota()
    {
        // Arrange: four of ten used when the period ended; what was left is lost.
        var quota = Standing.Quota(10, 4);

        // Act
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, TestData.EnabledIssuer(), true, quota.StandingAt(Standing.LicenseEnd));

        // Assert
        Assert.Equal((false, DomainErrors.QuotaExhausted.GetCode(), false, 10L, 4L, 0L), (state.CanIssue, state.ReasonCode, state.HasRoom, state.Limit, state.Used, state.Remaining));
    }

    [Fact]
    public void Event_Contract_IsPublishedOnTheIssuerWithItsNameAndVersion()
    {
        // Act: the topic other services subscribe to comes from this attribute; changing it leaves them listening to nobody.
        var key = Assert.Single(typeof(IssuingAvailabilityChangedDomainEvent).GetCustomAttributesData(), x => x.AttributeType.Name.StartsWith("EventKeyAttribute", StringComparison.Ordinal));

        // Assert
        Assert.Equal((typeof(IssuerAggregate), 1, "IssuingAvailabilityChangedDomainEvent"), (Assert.Single(key.AttributeType.GetGenericArguments()), Convert.ToInt32(key.ConstructorArguments[0].Value, System.Globalization.CultureInfo.InvariantCulture), (string?)key.ConstructorArguments[1].Value));
    }

    [Fact]
    public void Startup_Application_RegistersTheAnnouncerTheHandlersAskFor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        new CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Startup().Initialize(services, new ConfigurationBuilder().Build());

        // Assert
        Assert.Contains(services, x => x.ServiceType == typeof(IIssuingAvailabilityAnnouncer) && x.ImplementationType == typeof(IssuingAvailabilityAnnouncer) && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Of_SuspendedIssuerWithItsQuotaUsedUp_SaysTheIssuerFirst()
    {
        // Act: the reasons follow the order a new document is checked in.
        var state = IssuingAvailabilityAnnouncer.Of(TestData.Tenant, Standing.SuspendedIssuer(), true, Standing.Quota(1, 1).StandingAt(TestData.Now));

        // Assert
        Assert.Equal(DomainErrors.IssuerNotEnabled.GetCode(), state.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_TestProviderTheCountryDoesNotRouteWithoutPermission_IsNotPermitted()
    {
        // Arrange
        Store(TestData.EnabledIssuer());

        // Act
        var state = (IssuingAvailabilityChangedDomainEvent)await Announcer(Fakes.TestProviderOptions()).BuildAsync(TestData.Tenant, Standing.Quota(10), TestData.Now, CancellationToken.None);

        // Assert
        Assert.Equal((false, AppErrors.TestProviderNotGranted.GetCode()), (state.CanIssue, state.ReasonCode));
    }

    [Fact]
    public async Task BuildAsync_TestProviderWithPermission_CanIssue()
    {
        // Arrange
        Store(TestData.EnabledIssuer());
        grants.Setup(x => x.FindAsync<TestProviderGrantAggregate>(TestProviderGrantAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(TestProviderGrantAggregate.Grant(TestData.Tenant, TestData.User, TestData.Now));

        // Act
        var state = (IssuingAvailabilityChangedDomainEvent)await Announcer(Fakes.TestProviderOptions()).BuildAsync(TestData.Tenant, Standing.Quota(10), TestData.Now, CancellationToken.None);

        // Assert
        Assert.True(state.CanIssue);
    }

    [Fact]
    public async Task BuildAsync_ProviderTheCountryRoutes_NeedsNoPermission()
    {
        // Arrange
        Store(TestData.EnabledIssuer());

        // Act
        var state = (IssuingAvailabilityChangedDomainEvent)await Announcer().BuildAsync(TestData.Tenant, Standing.Quota(10), TestData.Now, CancellationToken.None);

        // Assert
        Assert.True(state.CanIssue);
    }

    [Fact]
    public async Task BuildAsync_TenantThatNeverHadAQuota_HasNoRoom()
    {
        // Arrange
        Store(TestData.EnabledIssuer());

        // Act
        var state = (IssuingAvailabilityChangedDomainEvent)await Announcer().BuildAsync(TestData.Tenant, null, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Equal((false, DomainErrors.QuotaExhausted.GetCode(), 0L, null), (state.CanIssue, state.ReasonCode, state.Limit, state.PeriodEnd));
    }

    [Fact]
    public async Task AnnounceAsync_Tenant_PublishesItsStateWithTheStoredQuota()
    {
        // Arrange
        Store(TestData.EnabledIssuer());
        Store(Standing.Quota(10, 10));

        // Act
        await Announcer().AnnounceAsync(TestData.Tenant, CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((false, 10L, 10L), (state.CanIssue, state.Limit, state.Used));
    }

    [Fact]
    public async Task AnnounceAsync_PlatformIssuerWithoutAStoredQuota_SaysTheReservationOfTheConfiguration()
    {
        // Arrange: the allowance of the platform's issuer is not a license; it is read from the configuration.
        Store(TestData.EnabledIssuer(tenant: TestData.PlatformTenant));

        // Act
        await Announcer().AnnounceAsync(TestData.PlatformTenant, CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((true, 500L, 500L), (state.CanIssue, state.Limit, state.Remaining));
    }

    [Fact]
    public async Task AnnounceForTestProviderAsync_IssuerWithATestProviderThatNeedsThePermission_Publishes()
    {
        // Arrange
        Store(TestData.EnabledIssuer());
        Store(Standing.Quota(10));

        // Act
        await Announcer(Fakes.TestProviderOptions()).AnnounceForTestProviderAsync(TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Equal(AppErrors.TestProviderNotGranted.GetCode(), Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published())).ReasonCode);
    }

    [Fact]
    public async Task AnnounceForTestProviderAsync_IssuerWhoseProviderNeedsNoPermission_PublishesNothing()
    {
        // Arrange: the permission does not change whether this issuer can issue.
        Store(TestData.EnabledIssuer());

        // Act
        await Announcer().AnnounceForTestProviderAsync(TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }

    [Fact]
    public async Task AnnounceForTestProviderAsync_TenantWithoutIssuer_PublishesNothing()
    {
        // Act
        await Announcer(Fakes.TestProviderOptions()).AnnounceForTestProviderAsync(TestData.Tenant, CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }
}

/// <summary>
/// The quota announces the state of the issuer when the room for a new document appears or disappears, and not on every
/// document.
/// </summary>
public class QuotaAvailabilityTest
{
    private readonly Mock<IDocumentQuotaRepository> repository = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly RecordingAvailability availability = new();

    public QuotaAvailabilityTest()
    {
        repository.EmulateOptimisticUpdate<IDocumentQuotaRepository, DocumentQuotaAggregate>();
    }

    private QuotaGate Gate() => new(repository.Object, availability, Fakes.Options(Fakes.Options()));

    private ApplyLicenseQuotaCommandHandler Licenses() => new(repository.Object, availability, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()));

    private DocumentQuotaAggregate Store(DocumentQuotaAggregate quota)
    {
        repository.Setup(x => x.FindAsync<DocumentQuotaAggregate>(quota.Id, It.IsAny<CancellationToken>())).ReturnsAsync(quota);

        return quota;
    }

    private static ApplyLicenseQuotaCommand Allowance(long documents, Instant? publishedAt = null) => new(TestData.Tenant, new Dictionary<string, string> { ["documents_key"] = documents.ToString(System.Globalization.CultureInfo.InvariantCulture) }, Standing.LicenseEnd, publishedAt ?? TestData.Now + Duration.FromMinutes(1));

    [Fact]
    public async Task ReserveAsync_TheLastUnit_ReturnsThatTheQuotaHasNoRoom()
    {
        // Arrange
        Store(Standing.Quota(10, 9));

        // Act
        var events = await Gate().ReserveAsync(TestData.Tenant, ElectronicDocumentType.Invoice, TestData.Now, CancellationToken.None);

        // Assert: with the threshold of 100 % for the person, the state for the services.
        var state = Assert.Single(events.OfType<IssuingAvailabilityChangedDomainEvent>());

        Assert.Equal((false, 10L, 10L, 0L, 1), (state.HasRoom, state.Limit, state.Used, state.Remaining, events.OfType<DocumentQuotaThresholdReachedDomainEvent>().Count(x => x.Percentage == 100)));
    }

    [Fact]
    public async Task ReserveAsync_NoteThatTakesTheLastUnit_ReturnsThatTheQuotaHasNoRoom()
    {
        // Arrange: a note needs no room, but it is counted, and this one leaves none for an invoice.
        Store(Standing.Quota(10, 9));

        // Act
        var events = await Gate().ReserveAsync(TestData.Tenant, ElectronicDocumentType.CreditNote, TestData.Now, CancellationToken.None);

        // Assert
        Assert.False(Assert.Single(events.OfType<IssuingAvailabilityChangedDomainEvent>()).HasRoom);
    }

    [Fact]
    public async Task ReserveAsync_UnitThatLeavesRoom_ReturnsNoState()
    {
        // Arrange
        Store(Standing.Quota(10, 3));

        // Act
        var events = await Gate().ReserveAsync(TestData.Tenant, ElectronicDocumentType.PosTicket, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Empty(events.OfType<IssuingAvailabilityChangedDomainEvent>());
    }

    [Fact]
    public async Task ReserveAsync_NoteAfterTheQuotaWasUsedUp_ReturnsNoStateAgain()
    {
        // Arrange: the notes keep counting past the limit; the answer was already «no».
        Store(Standing.Quota(10, 10));

        // Act
        var events = await Gate().ReserveAsync(TestData.Tenant, ElectronicDocumentType.CreditNote, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Empty(events.OfType<IssuingAvailabilityChangedDomainEvent>());
    }

    [Fact]
    public async Task ReserveAsync_FirstUnitOfARenewedPeriod_ReturnsNoState()
    {
        // Arrange: the period ended with the quota used up and the allowance had been renewed; there was room again from
        // the end of the period on, and there still is after this unit.
        var quota = Standing.Quota(10, 10);

        quota.ApplyAllowance(10, Standing.LicenseEnd + Duration.FromDays(365), TestData.Now, TestData.Now, TestData.SystemUser);
        Store(quota);

        // Act
        var events = await Gate().ReserveAsync(TestData.Tenant, ElectronicDocumentType.Invoice, Standing.LicenseEnd + Duration.FromDays(1), CancellationToken.None);

        // Assert
        Assert.Equal((1L, 0), (quota.Used, events.OfType<IssuingAvailabilityChangedDomainEvent>().Count()));
    }

    [Fact]
    public async Task ReleaseAsync_UnitGivenBackToAQuotaUsedUp_PublishesThatThereIsRoomAgain()
    {
        // Arrange
        Store(Standing.Quota(10, 10));

        // Act
        await Gate().ReleaseAsync(TestData.Tenant, TestData.Now, CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(availability.Published));

        Assert.Equal((true, 9L, 1L), (state.HasRoom, state.Used, state.Remaining));
    }

    [Fact]
    public async Task ReleaseAsync_UnitGivenBackToAQuotaWithRoom_PublishesNothing()
    {
        // Arrange
        Store(Standing.Quota(10, 4));

        // Act
        await Gate().ReleaseAsync(TestData.Tenant, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Empty(availability.Published);
    }

    [Fact]
    public async Task ReleaseAsync_UnitGivenBackToAQuotaStillOverItsLimit_PublishesNothing()
    {
        // Arrange: notes took it past the limit; one unit back does not make room.
        Store(Standing.Quota(10, 12));

        // Act
        await Gate().ReleaseAsync(TestData.Tenant, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Empty(availability.Published);
    }

    [Fact]
    public async Task ReleaseAsync_NothingToGiveBack_PublishesNothing()
    {
        // Arrange
        Store(Standing.Quota(0));

        // Act
        await Gate().ReleaseAsync(TestData.Tenant, TestData.Now, CancellationToken.None);

        // Assert
        Assert.Empty(availability.Published);
    }

    [Fact]
    public async Task ApplyLicenseQuota_BiggerAllowanceStillBelowWhatTheNotesCounted_PublishesThatThereIsStillNoRoom()
    {
        // Arrange: used up at 10, and three notes after it; the allowance grows to 12, which is still below what is used.
        Store(Standing.Quota(10, 13));

        // Act
        await Licenses().Handle(Allowance(12), CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((false, 12L, 13L, 0L), (state.HasRoom, state.Limit, state.Used, state.Remaining));
    }

    [Fact]
    public async Task ApplyLicenseQuota_AllowanceBelowWhatIsUsed_PublishesThatThereIsNoRoom()
    {
        // Arrange: nothing was used up; the license lowers the allowance below what is already used.
        Store(Standing.Quota(10, 6));

        // Act
        await Licenses().Handle(Allowance(5), CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((false, 5L, 6L, 0L), (state.HasRoom, state.Limit, state.Used, state.Remaining));
    }

    [Fact]
    public async Task ApplyLicenseQuota_BiggerAllowance_PublishesTheRoomItLeaves()
    {
        // Arrange
        Store(Standing.Quota(10, 10));

        // Act
        await Licenses().Handle(Allowance(25), CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal((true, 25L, 15L), (state.HasRoom, state.Limit, state.Remaining));
    }

    [Fact]
    public async Task ApplyLicenseQuota_RenewalBeforeThePeriodEnds_SaysUntilWhenItWasRenewed()
    {
        // Arrange
        var renewedUntil = Standing.LicenseEnd + Duration.FromDays(365);

        Store(Standing.Quota(10, 2));

        // Act
        await Licenses().Handle(new ApplyLicenseQuotaCommand(TestData.Tenant, new Dictionary<string, string> { ["documents_key"] = "10" }, renewedUntil, TestData.Now + Duration.FromMinutes(1)), CancellationToken.None);

        // Assert
        var state = Assert.IsType<IssuingAvailabilityChangedDomainEvent>(Assert.Single(pubsub.Published()));

        Assert.Equal(((Instant?)Standing.LicenseEnd, (Instant?)renewedUntil), (state.PeriodEnd, state.RenewedUntil));
    }

    [Fact]
    public async Task ApplyLicenseQuota_TheSameAllowanceAgain_PublishesNothing()
    {
        // Arrange: ms-tenants publishes the license again with nothing changed for this service.
        Store(Standing.AlreadySaved(Standing.Quota(10, 2)));

        // Act
        await Licenses().Handle(Allowance(10), CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }

    [Fact]
    public async Task ApplyLicenseQuota_AllowanceOlderThanTheOneApplied_PublishesNothing()
    {
        // Arrange
        Store(Standing.AlreadySaved(Standing.Quota(10, 2)));

        // Act
        await Licenses().Handle(Allowance(99, TestData.Now - Duration.FromDays(1)), CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }
}

/// <summary>
/// The issuer announces its state when a change makes it able or unable to issue, and not on any other change.
/// </summary>
public class IssuerAvailabilityTest
{
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly RecordingAvailability availability = new();

    public IssuerAvailabilityTest()
    {
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
    }

    private Task<IssuerAggregate> ChangeAsync(IssuerAggregate issuer, Action<IssuerAggregate> change)
    {
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        return IssuerWrites.UpdateAsync(issuers.Object, pubsub.Object, availability, issuer.Tenant, change, CancellationToken.None);
    }

    [Fact]
    public async Task UpdateAsync_EnabledIssuerSuspended_AnnouncesItsState()
    {
        // Act
        await ChangeAsync(TestData.EnabledIssuer(), x => x.Suspend("230", TestData.User));

        // Assert
        Assert.Equal([TestData.Tenant], availability.Announced);
    }

    [Fact]
    public async Task UpdateAsync_IssuerThatStartsItsTests_AnnouncesItsState()
    {
        // Act: starting the tests publishes no event of its own, and from then on the issuer can issue.
        var issuer = await ChangeAsync(Standing.IssuerWithMandate(), x => x.StartTesting("0e3d0669-03ed-4f70-a237-c9541d22c5a1", TestData.User));

        // Assert
        Assert.Equal((true, 1, 0), (issuer.CanIssue, availability.Announced.Count, pubsub.Published().Count));
    }

    [Fact]
    public async Task UpdateAsync_SuspendedIssuerThatTriesToStartItsTests_IsRejectedAndAnnouncesNothing()
    {
        // Act: a suspended issuer does not go back to its tests; it only issues again when it is enabled.
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => ChangeAsync(Standing.SuspendedIssuer(), x => x.StartTesting("0e3d0669-03ed-4f70-a237-c9541d22c5a1", TestData.User)));

        // Assert
        Assert.Equal((DomainErrors.IssuerStatusDoesNotAllowIt.GetCode(), 0), (exception.Code, availability.Announced.Count));
    }

    [Fact]
    public async Task UpdateAsync_SuspendedIssuerEnabledAgain_AnnouncesItsState()
    {
        // Act
        await ChangeAsync(Standing.SuspendedIssuer(), x => x.Enable(IssuerEnvironment.Test, false, TestData.User));

        // Assert
        Assert.Single(availability.Announced);
    }

    [Fact]
    public async Task UpdateAsync_IssuerInItsTestsEnabled_AnnouncesNothingBecauseItAlreadyCouldIssue()
    {
        // Arrange
        var issuer = Standing.IssuerWithMandate();

        issuer.StartTesting("0e3d0669-03ed-4f70-a237-c9541d22c5a1", TestData.User);

        // Act
        await ChangeAsync(issuer, x => x.Enable(IssuerEnvironment.Test, false, TestData.User));

        // Assert
        Assert.Empty(availability.Announced);
    }

    [Fact]
    public async Task UpdateAsync_FiscalDataOfAnEnabledIssuer_AnnouncesNothing()
    {
        // Act
        await ChangeAsync(TestData.EnabledIssuer(), x => x.UpdateFiscalData(TestData.FiscalData(), TestData.User));

        // Assert
        Assert.Empty(availability.Announced);
    }

    [Fact]
    public async Task UpdateAsync_IssuerAlreadySuspendedSuspendedAgain_AnnouncesNothing()
    {
        // Act
        await ChangeAsync(Standing.SuspendedIssuer(), x => x.Suspend("230", TestData.User));

        // Assert
        Assert.Empty(availability.Announced);
    }
}
