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

    private static NumberingRangeAggregate Range(long from = 1, long to = 100) => NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(from: from, to: to), NumberingSource.Platform, TestData.User);

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
    public void Create_PrefixWithASymbol_ThrowsPrefixIsInvalid()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => NumberingRangeAggregate.Create(TestData.Tenant, Guid.NewGuid(), TestData.Resolution(prefix: "SE-T"), NumberingSource.Platform, TestData.User));

        // Assert
        Assert.Equal(Errors.PrefixIsInvalid.GetCode(), exception.Code);
    }
}
