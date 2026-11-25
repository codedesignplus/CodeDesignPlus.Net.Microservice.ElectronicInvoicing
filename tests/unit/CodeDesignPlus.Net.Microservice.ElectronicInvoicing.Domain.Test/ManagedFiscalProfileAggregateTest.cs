namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// The fiscal data another service decides for the issuer of a tenant (Kappali pendings/752: the condition of VAT the
/// copropiedad declares in Organización).
/// </summary>
public class ManagedFiscalProfileAggregateTest
{
    private static readonly LocalDate Declared = new(2026, 11, 25);

    private static ManagedFiscalProfileEntry Responsible(LocalDate? from = null, params string[] responsibilities) => new(from ?? Declared, "48", [.. responsibilities]);

    private static ManagedFiscalProfileAggregate Profile(params ManagedFiscalProfileEntry[] entries) => ManagedFiscalProfileAggregate.Create(TestData.Tenant, "America/Bogota", ["O-23"], entries.Length == 0 ? [Responsible()] : entries, TestData.SystemUser);

    [Fact]
    public void InForceOn_DayBeforeTheFirstVersion_IsNull()
    {
        // Act
        var entry = Profile().InForceOn(Declared.PlusDays(-1));

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public void InForceOn_VersionOfALaterDay_KeepsTheEarlierUntilThatDay()
    {
        // Arrange
        var profile = Profile(new ManagedFiscalProfileEntry(new LocalDate(2026, 1, 1), "49", []), Responsible(new LocalDate(2026, 12, 1), "O-23"));

        // Act
        var before = profile.InForceOn(new LocalDate(2026, 11, 30))!.TaxRegimeCode;
        var after = profile.InForceOn(new LocalDate(2026, 12, 1))!.TaxRegimeCode;

        // Assert
        Assert.Equal(("49", "48"), (before, after));
    }

    [Fact]
    public void InForceAt_LateEveningInBogota_ReadsTheDayOfTheTenant()
    {
        // Arrange: 2026-11-25 03:00 UTC is still the 24th in Bogota.
        var profile = Profile(new ManagedFiscalProfileEntry(new LocalDate(2026, 1, 1), "49", []), Responsible());

        // Act
        var entry = profile.InForceAt(Instant.FromUtc(2026, 11, 25, 3, 0));

        // Assert
        Assert.Equal("49", entry!.TaxRegimeCode);
    }

    [Fact]
    public void ResponsibilitiesWith_OwnAndManaged_ReplacesTheManagedAndDropsTheCodeOfNone()
    {
        // Arrange
        var profile = Profile(Responsible(null, "O-23"));

        // Act: the issuer had R-99-PN and O-23 on its own; O-23 is decided by the history and R-99-PN goes with a real one.
        var codes = profile.ResponsibilitiesWith(["R-99-PN", "O-23", "O-15"], profile.InForceOn(Declared)!, "R-99-PN");

        // Assert
        Assert.Equal(["O-15", "O-23"], codes);
    }

    [Fact]
    public void ResponsibilitiesWith_ManagedNoLongerInForce_RemovesItAndLeavesTheCodeOfNone()
    {
        // Arrange: the copropiedad stopped being a withholding agent.
        var profile = Profile(Responsible(null));

        // Act
        var codes = profile.ResponsibilitiesWith(["O-23"], profile.InForceOn(Declared)!, "R-99-PN");

        // Assert
        Assert.Equal(["R-99-PN"], codes);
    }

    [Fact]
    public void Create_WithoutEntries_ThrowsManagedFiscalProfileWithoutEntries()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ManagedFiscalProfileAggregate.Create(TestData.Tenant, "America/Bogota", ["O-23"], [], TestData.SystemUser));

        // Assert
        Assert.Equal(Errors.ManagedFiscalProfileWithoutEntries.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_TwoVersionsOfTheSameDay_ThrowsManagedEntriesRepeatADay()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Profile(Responsible(), Responsible()));

        // Assert
        Assert.Equal(Errors.ManagedEntriesRepeatADay.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_ResponsibilityItDoesNotManage_ThrowsManagedResponsibilityNotDeclared()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Profile(Responsible(null, "O-13")));

        // Assert
        Assert.Equal(Errors.ManagedResponsibilityNotDeclared.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_VersionWithoutRegime_ThrowsManagedEntryWithoutTaxRegime()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Profile(new ManagedFiscalProfileEntry(Declared, " ", [])));

        // Assert
        Assert.Equal(Errors.ManagedEntryWithoutTaxRegime.GetCode(), exception.Code);
    }

    [Fact]
    public void Create_PlatformTenant_ThrowsManagedFiscalProfileNotForThePlatform()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => ManagedFiscalProfileAggregate.Create(TestData.PlatformTenant, "America/Bogota", [], [Responsible()], TestData.SystemUser));

        // Assert
        Assert.Equal(Errors.ManagedFiscalProfileNotForThePlatform.GetCode(), exception.Code);
    }

    [Fact]
    public void Replace_SameHistoryInAnotherOrder_ChangesNothing()
    {
        // Arrange
        var first = new ManagedFiscalProfileEntry(new LocalDate(2026, 1, 1), "49", []);
        var profile = Profile(first, Responsible(null, "O-23"));

        // Act
        var changed = profile.Replace("America/Bogota", ["O-23"], [Responsible(null, "O-23"), first], TestData.SystemUser);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void Replace_NewVersion_ChangesTheHistory()
    {
        // Arrange
        var profile = Profile();

        // Act
        var changed = profile.Replace("America/Bogota", ["O-23"], [Responsible(), Responsible(new LocalDate(2027, 1, 1), "O-23")], TestData.SystemUser);

        // Assert
        Assert.Equal((true, 2), (changed, profile.Entries.Count));
    }

    [Theory]
    [InlineData(new string[0], "R-99-PN")]
    [InlineData(new[] { "R-99-PN", "O-13" }, "O-13")]
    [InlineData(new[] { "O-13", "o-13", " O-15 " }, "O-13,O-15")]
    public void Normalize_Codes_KeepsTheCodeOfNoneOnlyAlone(string[] codes, string expected)
    {
        // Act
        var normalized = FiscalResponsibilities.Normalize(codes, "R-99-PN");

        // Assert
        Assert.Equal(expected, string.Join(',', normalized));
    }

    [Fact]
    public void Normalize_CountryWithoutCodeOfNone_LeavesAnEmptyList()
    {
        // Act
        var normalized = FiscalResponsibilities.Normalize([], null);

        // Assert
        Assert.Empty(normalized);
    }
}

/// <summary>
/// How the issuer follows the fiscal data decided outside it (Kappali pendings/752).
/// </summary>
public class IssuerFollowsFiscalDataTest
{
    [Fact]
    public void FollowFiscalData_OtherRegime_TakesItAndTheResponsibilities()
    {
        // Arrange
        var issuer = TestData.RegisteredIssuer();

        // Act
        var changed = issuer.FollowFiscalData("49", ["R-99-PN"], TestData.SystemUser);

        // Assert
        Assert.Equal((true, "49", "R-99-PN"), (changed, issuer.TaxRegimeCode, string.Join(',', issuer.TaxResponsibilities)));
    }

    [Fact]
    public void FollowFiscalData_SameDataInAnotherOrder_WritesNothing()
    {
        // Arrange
        var issuer = IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData() with { TaxResponsibilities = ["O-13", "O-23"] }, "Simulated", TestData.User, TestData.CheckDigitRules());

        // Act
        var changed = issuer.FollowFiscalData("48", ["O-23", "O-13"], TestData.SystemUser);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void RecordFiscalDataRejection_EnabledIssuer_StopsIssuing()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        // Act
        issuer.RecordFiscalDataRejection("318", TestData.SystemUser);

        // Assert
        Assert.Equal(("318", false), (issuer.FiscalDataRejectionCode, issuer.CanIssue));
    }

    [Fact]
    public void FollowFiscalData_AfterARejection_IssuesAgain()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();
        issuer.RecordFiscalDataRejection("318", TestData.SystemUser);

        // Act: the provider accepted the same data on the next round.
        var changed = issuer.FollowFiscalData(issuer.TaxRegimeCode, issuer.TaxResponsibilities, TestData.SystemUser);

        // Assert
        Assert.Equal((true, null, true), (changed, issuer.FiscalDataRejectionCode, issuer.CanIssue));
    }
}
