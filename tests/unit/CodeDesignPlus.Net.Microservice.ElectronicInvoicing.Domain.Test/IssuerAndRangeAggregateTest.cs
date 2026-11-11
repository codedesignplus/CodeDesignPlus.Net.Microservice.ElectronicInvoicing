namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

public class IssuerAggregateTest
{
    [Fact]
    public void IdFor_SameTenant_IsAlwaysTheSame()
    {
        // Act & Assert
        Assert.Equal(IssuerAggregate.IdFor(TestData.Tenant), TestData.RegisteredIssuer().Id);
    }

    [Theory]
    [InlineData("900000002-12")]
    [InlineData("900000002--1")]
    [InlineData("")]
    public void Register_TaxIdWithoutItsHyphen_ThrowsTaxIdIsInvalid(string taxId)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData() with { TaxId = taxId }, "Simulated", TestData.User));

        // Assert
        Assert.Equal(Errors.TaxIdIsInvalid.GetCode(), exception.Code);
    }

    [Fact]
    public void Register_UnknownTimeZone_ThrowsTimeZoneIsInvalid()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData() with { TimeZoneId = "Mars/Olympus" }, "Simulated", TestData.User));

        // Assert
        Assert.Equal(Errors.TimeZoneIsInvalid.GetCode(), exception.Code);
    }

    [Fact]
    public void CanIssue_EnabledWithoutMandate_IsFalse()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.Enable(IssuerEnvironment.Production, TestData.User));

        // Assert
        Assert.Equal((Errors.MandateNotAccepted.GetCode(), false), (exception.Code, issuer.CanIssue));
    }

    [Fact]
    public void StartTesting_WithMandate_SendsToHabilitationAndCanIssue()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        // Act
        issuer.StartTesting("0e3d0669-03ed-4f70-a237-c9541d22c5a1", TestData.User);

        // Assert
        Assert.Equal((IssuerStatus.Testing, IssuerEnvironment.Habilitation, true), (issuer.Status, issuer.Environment, issuer.CanIssue));
    }

    [Fact]
    public void StartTesting_TestSetThatIsNotAUuid_ThrowsTestSetIdIsRequired()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.StartTesting("not-a-uuid", TestData.User));

        // Assert
        Assert.Equal(Errors.TestSetIdIsRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Enable_WithMandate_AnnouncesIt()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        // Act
        issuer.Enable(IssuerEnvironment.Production, TestData.User);

        // Assert
        var enabled = Assert.IsType<IssuerEnabledDomainEvent>(issuer.GetAndClearEvents().Single());

        Assert.Equal((IssuerEnvironment.Production, TestData.Tenant), (enabled.Environment, enabled.Tenant));
    }

    [Fact]
    public void Enable_ToHabilitation_ThrowsIssuerStatusDoesNotAllowIt()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.Enable(IssuerEnvironment.Habilitation, TestData.User));

        // Assert
        Assert.Equal(Errors.IssuerStatusDoesNotAllowIt.GetCode(), exception.Code);
    }

    [Fact]
    public void RecordCredentialRejection_UpToTheThreshold_SuspendsOnceAndAnnouncesIt()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        // Act
        issuer.RecordCredentialRejection(3, "302", TestData.SystemUser);
        issuer.RecordCredentialRejection(3, "302", TestData.SystemUser);
        var beforeThreshold = issuer.Status;
        issuer.RecordCredentialRejection(3, "302", TestData.SystemUser);
        issuer.RecordCredentialRejection(3, "302", TestData.SystemUser);

        // Assert
        Assert.Equal((IssuerStatus.Enabled, IssuerStatus.Suspended, 1, false), (beforeThreshold, issuer.Status, issuer.GetAndClearEvents().OfType<IssuerSuspendedDomainEvent>().Count(), issuer.CanIssue));
    }

    [Fact]
    public void ChooseProvider_Provisioned_ThrowsIssuerStatusDoesNotAllowIt()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        issuer.MarkProvisioned("child-1", TestData.User);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.ChooseProvider("Aliaddo", TestData.User));

        // Assert
        Assert.Equal(Errors.IssuerStatusDoesNotAllowIt.GetCode(), exception.Code);
    }

    [Fact]
    public void ConnectCredentials_Replaced_RaisesTheVersion()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();
        var secrets = new SealedIssuerSecrets(new() { ["token"] = "vault:v1:abc" }, new() { ["token"] = "wxyz" }, 0);

        // Act
        issuer.ConnectCredentials(secrets, [], TestData.User);
        issuer.ConnectCredentials(secrets, [], TestData.User);

        // Assert
        Assert.Equal(2, issuer.Credentials!.Version);
    }
}

public class NumberingRangeAggregateTest
{
    private static readonly LocalDate Today = new(2026, 11, 4);

    private static NumberingRangeAggregate Range(long from = 1, long to = 100)
    {
        var range = NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(from: from, to: to), NumberingSource.Platform, TestData.User);

        range.GetAndClearEvents();

        return range;
    }

    [Fact]
    public void Reserve_Twice_GivesConsecutiveNumbers()
    {
        // Arrange
        var range = Range();

        // Act
        var first = range.Reserve(Today);
        var second = range.Reserve(Today);

        // Assert
        Assert.Equal((1L, 2L, 3L), (first.Consecutive, second.Consecutive, range.Next));
    }

    [Fact]
    public void Reserve_NoNumbersLeft_ThrowsNumberingRangeExhausted()
    {
        // Arrange
        var range = Range(1, 1);

        range.Reserve(Today);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => range.Reserve(Today));

        // Assert
        Assert.Equal(Errors.NumberingRangeExhausted.GetCode(), exception.Code);
    }

    [Fact]
    public void Reserve_AfterItsValidity_ThrowsNumberingRangeExpired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Range().Reserve(new LocalDate(2028, 1, 1)));

        // Assert
        Assert.Equal(Errors.NumberingRangeExpired.GetCode(), exception.Code);
    }

    [Fact]
    public void Reserve_BeforeItsValidity_ThrowsNumberingRangeNotValidYet()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Range().Reserve(new LocalDate(2025, 12, 31)));

        // Assert
        Assert.Equal(Errors.NumberingRangeNotValidYet.GetCode(), exception.Code);
    }

    [Fact]
    public void Reserve_RangeTheProviderNumbers_ThrowsNumberingRangeIsInvalid()
    {
        // Arrange
        var range = NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(), NumberingSource.Provider, TestData.User);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => range.Reserve(Today));

        // Assert
        Assert.Equal(Errors.NumberingRangeIsInvalid.GetCode(), exception.Code);
    }

    [Fact]
    public void CheckRunningOut_FewNumbersLeft_AnnouncesItOnce()
    {
        // Arrange: 100 numbers, 91 used, 9 left: below 10 %.
        var range = Range();

        for (var i = 0; i < 91; i++)
        {
            range.Reserve(Today);
        }

        // Act
        range.CheckRunningOut(Today, 10, 30);
        range.CheckRunningOut(Today, 10, 30);

        // Assert
        var alert = Assert.IsType<NumberingRangeRunningOutDomainEvent>(range.GetAndClearEvents().Single());

        Assert.Equal((NumberingRangeAlert.Numbers, 9L), (alert.Alert, alert.Remaining));
    }

    [Fact]
    public void CheckRunningOut_ValidityEndingWithinTheDays_RaisesTheValidityAlert()
    {
        // Arrange
        var range = Range();

        // Act
        var raised = range.CheckRunningOut(new LocalDate(2027, 12, 15), 10, 30);

        // Assert
        Assert.Equal((true, NumberingRangeAlert.Validity), (raised, Assert.IsType<NumberingRangeRunningOutDomainEvent>(range.GetAndClearEvents().Single()).Alert));
    }

    [Fact]
    public void Refresh_ExtendedByTheProvider_KeepsTheNextNumberAndForgetsTheAlerts()
    {
        // Arrange
        var range = Range();

        range.Reserve(Today);
        range.CheckRunningOut(new LocalDate(2027, 12, 15), 10, 30);

        // Act
        var refreshed = range.Refresh(TestData.Resolution(from: 1, to: 100) with { ValidUntil = new LocalDate(2029, 12, 31) }, TestData.User);

        // Assert
        Assert.Equal((true, 2L, 0), (refreshed, range.Next, range.Alerts.Count));
    }

    [Fact]
    public void IdFor_SameResolution_IsTheSameRange()
    {
        // Arrange
        var issuer = Guid.NewGuid();

        // Act & Assert
        Assert.Equal(NumberingRangeAggregate.IdFor(issuer, ElectronicDocumentType.Invoice, "SETP", "1"), NumberingRangeAggregate.IdFor(issuer, ElectronicDocumentType.Invoice, "SETP", "1"));
    }

    [Fact]
    public void Create_NewRange_AnnouncesItsWholeResolution()
    {
        // Arrange
        var issuer = Guid.NewGuid();

        // Act
        var range = NumberingRangeAggregate.Create(TestData.Tenant, issuer, TestData.Resolution(prefix: "FAC", from: 1, to: 5000), NumberingSource.Platform, TestData.User);

        // Assert
        var registered = Assert.IsType<NumberingRangeRegisteredDomainEvent>(range.GetAndClearEvents().Single());

        Assert.Equal(
            (range.Id, TestData.Tenant, false, issuer, ElectronicDocumentType.Invoice, "FAC", "18760000001", "technical-key-of-test", 1L, 5000L, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31)),
            (registered.AggregateId, registered.Tenant, registered.PlatformIssuer, registered.IssuerId, registered.DocumentType, registered.Prefix, registered.ResolutionNumber, registered.TechnicalKey, registered.From, registered.To, registered.ValidFrom, registered.ValidUntil));
    }

    [Fact]
    public void Create_RangeOfThePlatformsIssuer_SaysItIsThePlatforms()
    {
        // Act
        var range = NumberingRangeAggregate.Create(PlatformScope.Tenant, Guid.NewGuid(), TestData.Resolution(), NumberingSource.Platform, TestData.User);

        // Assert
        Assert.True(Assert.IsType<NumberingRangeRegisteredDomainEvent>(range.GetAndClearEvents().Single()).PlatformIssuer);
    }

    [Fact]
    public void Refresh_ResolutionChanged_AnnouncesItAgain()
    {
        // Arrange
        var range = Range();

        // Act
        range.Refresh(TestData.Resolution(from: 1, to: 200), TestData.User);

        // Assert
        Assert.Equal(200L, Assert.IsType<NumberingRangeRegisteredDomainEvent>(range.GetAndClearEvents().Single()).To);
    }

    [Fact]
    public void Refresh_SameResolution_AnnouncesNothing()
    {
        // Arrange
        var range = Range();

        // Act
        range.Refresh(TestData.Resolution(from: 1, to: 100), TestData.User);

        // Assert
        Assert.Empty(range.GetAndClearEvents());
    }

    [Fact]
    public void CheckRunningOut_CallersUsedNumbersPastTheNext_CountsFromTheHighestUsed()
    {
        // Arrange: 100 numbers, none reserved here, but a caller already gave up to the 95th: 5 left, below 10 %.
        var range = Range();

        // Act
        range.CheckRunningOut(Today, 10, 30, 95);

        // Assert
        var alert = Assert.IsType<NumberingRangeRunningOutDomainEvent>(range.GetAndClearEvents().Single());

        Assert.Equal((NumberingRangeAlert.Numbers, 5L), (alert.Alert, alert.Remaining));
    }

    [Fact]
    public void CheckRunningOut_HighestUsedBehindTheNext_CountsFromTheNext()
    {
        // Arrange: 91 reserved here; an old document carries the 10th.
        var range = Range();

        for (var i = 0; i < 91; i++)
        {
            range.Reserve(Today);
        }

        // Act
        range.CheckRunningOut(Today, 10, 30, 10);

        // Assert
        Assert.Equal(9L, Assert.IsType<NumberingRangeRunningOutDomainEvent>(range.GetAndClearEvents().Single()).Remaining);
    }

    [Fact]
    public void CheckRunningOut_CallersUsedFewNumbers_RaisesNothing()
    {
        // Arrange
        var range = Range();

        // Act
        var raised = range.CheckRunningOut(Today, 10, 30, 50);

        // Assert
        Assert.False(raised);
    }

    [Fact]
    public void Covers_NumberOfItsPrefixInsideTheRange_IsTrue()
    {
        // Arrange
        var range = Range();

        // Act & Assert
        Assert.Equal((true, false, false, false), (range.Covers(new DocumentNumber("SETP", 100)), range.Covers(new DocumentNumber("SETP", 101)), range.Covers(new DocumentNumber("SETP", 0)), range.Covers(new DocumentNumber("FAC", 50))));
    }

    [Theory]
    [InlineData("SETPX")]
    [InlineData("SE-T")]
    [InlineData("")]
    [InlineData("FÑ")]
    [InlineData("F C")]
    public void Create_PrefixThatIsNotOneToFourLettersOrDigits_ThrowsPrefixIsInvalid(string prefix)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(prefix: prefix), NumberingSource.Platform, TestData.User));

        // Assert
        Assert.Equal(Errors.PrefixIsInvalid.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_PrefixOfFourLettersAndDigits_IsKept()
    {
        // Act
        var range = NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(prefix: "FE01"), NumberingSource.Platform, TestData.User);

        // Assert
        Assert.Equal("FE01", range.Prefix);
    }

    [Fact]
    public void Create_PrefixWithASymbol_ThrowsPrefixIsInvalid()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(prefix: "SE-T"), NumberingSource.Platform, TestData.User));

        // Assert
        Assert.Equal(Errors.PrefixIsInvalid.GetCode(), exception.Code);
    }
}
