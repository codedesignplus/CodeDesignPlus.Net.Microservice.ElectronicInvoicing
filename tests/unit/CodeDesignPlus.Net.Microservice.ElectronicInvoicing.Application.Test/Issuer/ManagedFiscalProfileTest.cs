using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.RegisterIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands.UpdateIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuerOptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

/// <summary>
/// The issuer follows the tax regime and the responsibilities another service decides for its tenant (Kappali
/// pendings/752: the copropiedad declares its condition of VAT in Organización and the issuer follows it).
/// </summary>
public class ManagedFiscalProfileTest
{
    /// <summary>
    /// The day the copropiedad declared itself responsible for VAT and withholding agent, before <see cref="TestData.Now"/>.
    /// </summary>
    private static readonly LocalDate Declared = new(2026, 11, 1);

    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IManagedFiscalProfileRepository> profiles = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IIssuerProvisioningProvider> provisioning = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly RecordingAvailability availability = new();

    private IssuerAggregate? stored;
    private ManagedFiscalProfileAggregate? profile;

    public ManagedFiscalProfileTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provisioning.Setup(x => x.Code).Returns("Simulated");
        provisioning.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Find("Simulated")).Returns(provisioning.Object);
        factory.Setup(x => x.Get("Simulated")).Returns(provisioning.Object);
        factory.Setup(x => x.FindProvisioning("Simulated")).Returns(provisioning.Object);

        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(() => stored!);
        issuers.Setup(x => x.ExistsAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(() => stored is not null);
        issuers.Setup(x => x.CreateIfAbsentAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>())).Callback<IssuerAggregate, CancellationToken>((x, _) => stored = x).ReturnsAsync(true);
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();

        profiles.Setup(x => x.FindAsync<ManagedFiscalProfileAggregate>(ManagedFiscalProfileAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(() => profile!);
        profiles.Setup(x => x.UpsertAsync(It.IsAny<ManagedFiscalProfileAggregate>(), It.IsAny<CancellationToken>())).Callback<ManagedFiscalProfileAggregate, CancellationToken>((x, _) => profile = x).Returns(Task.CompletedTask);
        profiles.Setup(x => x.FindTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => profile is null ? [] : [profile.Tenant]);
    }

    /// <summary>
    /// Los Guayacanes: registered as not responsible for VAT before declaring itself responsible in Organización.
    /// </summary>
    private static IssuerAggregate NotResponsibleIssuer(bool provisioned = true)
    {
        var issuer = IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData() with { TaxRegimeCode = "49", TaxResponsibilities = ["R-99-PN"] }, "Simulated", TestData.User, TestData.CheckDigitRules());

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        if (provisioned)
        {
            issuer.MarkProvisioned("child-1", TestData.User);
            issuer.Enable(IssuerEnvironment.Test, true, TestData.User);
        }

        return issuer;
    }

    private static ManagedFiscalProfileAggregate Responsible(bool withholdingAgent = true, LocalDate? from = null) => ManagedFiscalProfileAggregate.Create(TestData.Tenant, "America/Bogota", ["O-23"], [new ManagedFiscalProfileEntry(from ?? Declared, "48", withholdingAgent ? ["O-23"] : [])], TestData.SystemUser);

    private IssuerFiscalDataFollower Follower() => new(issuers.Object, profiles.Object, factory.Object, pubsub.Object, availability, Fakes.Clock(), Fakes.Options(Fakes.Options()), NullLogger<IssuerFiscalDataFollower>.Instance);

    private static UpdateIssuerCommand Update(string? regime, params string[] responsibilities) => new("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", regime, [.. responsibilities], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota");

    private UpdateIssuerCommandHandler UpdateHandler() => new(issuers.Object, pubsub.Object, availability, factory.Object, profiles.Object, Fakes.Clock(), user.Object, Fakes.Options(Fakes.Options()));

    private static RegisterIssuerCommand Register(string? regime, params string[] responsibilities) => new("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", regime, [.. responsibilities], TestData.Address(), "emisor@example.com", "3000000001", "America/Bogota", null);

    private RegisterIssuerCommandHandler RegisterHandler() => new(issuers.Object, factory.Object, Mock.Of<ITestProviderGrantRepository>(), profiles.Object, Fakes.Clock(), user.Object, Fakes.Options(Fakes.Options()));

    private GetIssuerQueryHandler GetHandler() => new(issuers.Object, factory.Object, Mock.Of<IQuotaGate>(), Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), profiles.Object);

    private static CodeDesignPlusException Refusal() => new(Layer.Infrastructure, "318", "refused");

    // ─── The follower ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_ProvisionedIssuerBehindTheHistory_SendsItToTheProviderAndKeepsIt()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();

        // Act
        var result = await Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.UpdateIssuerAsync(It.Is<ProviderIssuer>(i => i.TaxRegimeCode == "48" && i.TaxResponsibilities.SequenceEqual(new[] { "O-23" })), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((true, "48", "O-23"), (result.Applied, stored.TaxRegimeCode, string.Join(',', stored.TaxResponsibilities)));
    }

    [Fact]
    public async Task SyncAsync_ProviderRefusesTheData_KeepsTheOldOnesAndStopsIssuing()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(Refusal());

        // Act
        var result = await Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None);

        // Assert
        Assert.Equal(("318", "49", "318", false), (result.Rejection?.Code, stored.TaxRegimeCode, stored.FiscalDataRejectionCode, stored.CanIssue));
    }

    [Fact]
    public async Task SyncAsync_ProviderDoesNotAnswer_ThrowsAndRecordsNothing()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "down"));

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None));

        // Assert
        Assert.Equal(("301", "49", null), (exception.Code, stored.TaxRegimeCode, stored.FiscalDataRejectionCode));
    }

    [Fact]
    public async Task SyncAsync_VersionOfALaterDay_LeavesTheIssuerUntilThatDay()
    {
        // Arrange: declared on the 4th for the 5th; TestData.Now is the 4th in Bogota.
        stored = NotResponsibleIssuer();
        profile = Responsible(from: new LocalDate(2026, 11, 5));

        // Act
        var result = await Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal((false, "49"), (result.Applied, stored.TaxRegimeCode));
    }

    [Fact]
    public async Task SyncAsync_IssuerNotInTheProviderYet_TakesTheDataWithoutCallingIt()
    {
        // Arrange
        stored = NotResponsibleIssuer(provisioned: false);
        profile = Responsible();

        // Act
        await Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("48", stored.TaxRegimeCode);
    }

    [Fact]
    public async Task SyncAsync_IssuerAlreadyAligned_DoesNotCallTheProvider()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        stored.FollowFiscalData("48", ["O-23"], TestData.SystemUser);
        profile = Responsible();

        // Act
        var result = await Follower().SyncAsync(TestData.Tenant, TestData.SystemUser, CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(result.Applied);
    }

    // ─── The push of the owner (gRPC) ─────────────────────────────────────────

    [Fact]
    public async Task SetManagedFiscalProfile_IssuerRegistered_KeepsTheHistoryAndAlignsTheIssuer()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        var handler = new SetManagedFiscalProfileCommandHandler(profiles.Object, Follower(), Fakes.Options(Fakes.Options()), NullLogger<SetManagedFiscalProfileCommandHandler>.Instance);

        // Act
        await handler.Handle(new SetManagedFiscalProfileCommand(TestData.Tenant, "America/Bogota", ["O-23"], [new ManagedFiscalProfileEntry(Declared, "48", ["O-23"])]), CancellationToken.None);

        // Assert
        Assert.Equal((1, "48"), (profile!.Entries.Count, stored.TaxRegimeCode));
    }

    [Fact]
    public async Task SetManagedFiscalProfile_ProviderDoesNotAnswer_KeepsTheHistoryForTheRound()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CodeDesignPlusException(Layer.Infrastructure, "301", "down"));
        var handler = new SetManagedFiscalProfileCommandHandler(profiles.Object, Follower(), Fakes.Options(Fakes.Options()), NullLogger<SetManagedFiscalProfileCommandHandler>.Instance);

        // Act
        await handler.Handle(new SetManagedFiscalProfileCommand(TestData.Tenant, "America/Bogota", ["O-23"], [new ManagedFiscalProfileEntry(Declared, "48", ["O-23"])]), CancellationToken.None);

        // Assert
        Assert.Equal(("48", "49"), (profile!.Entries[0].TaxRegimeCode, stored.TaxRegimeCode));
    }

    [Fact]
    public async Task SetManagedFiscalProfile_SameHistoryAgain_DoesNotWriteIt()
    {
        // Arrange
        profile = Responsible();
        var handler = new SetManagedFiscalProfileCommandHandler(profiles.Object, Follower(), Fakes.Options(Fakes.Options()), NullLogger<SetManagedFiscalProfileCommandHandler>.Instance);

        // Act
        await handler.Handle(new SetManagedFiscalProfileCommand(TestData.Tenant, "America/Bogota", ["O-23"], [new ManagedFiscalProfileEntry(Declared, "48", ["O-23"])]), CancellationToken.None);

        // Assert
        profiles.Verify(x => x.UpdateAsync(It.IsAny<ManagedFiscalProfileAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── «Actualizar desde Organización» (REST) ─────────────────────────────────

    [Fact]
    public async Task SyncIssuerFiscalProfile_NoHistoryInForce_ThrowsManagedFiscalProfileNotReceived()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        var handler = new SyncIssuerFiscalProfileCommandHandler(issuers.Object, profiles.Object, Follower(), Fakes.Clock(), user.Object);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new SyncIssuerFiscalProfileCommand(), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.ManagedFiscalProfileNotReceived.GetCode(), exception.Code);
    }

    [Fact]
    public async Task SyncIssuerFiscalProfile_ProviderRefuses_ThrowsItsRefusal()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(Refusal());
        var handler = new SyncIssuerFiscalProfileCommandHandler(issuers.Object, profiles.Object, Follower(), Fakes.Clock(), user.Object);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => handler.Handle(new SyncIssuerFiscalProfileCommand(), CancellationToken.None));

        // Assert
        Assert.Equal(("318", "318"), (exception.Code, stored.FiscalDataRejectionCode));
    }

    // ─── The round of the job ───────────────────────────────────────────────────

    [Fact]
    public async Task ApplyManagedFiscalProfiles_ProviderRefusesAnIssuer_FailsTheRound()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(Refusal());
        var handler = new ApplyManagedFiscalProfilesCommandHandler(profiles.Object, Follower(), Fakes.Options(Fakes.Options()), NullLogger<ApplyManagedFiscalProfilesCommandHandler>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new ApplyManagedFiscalProfilesCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task ApplyManagedFiscalProfiles_IssuerBehind_AlignsIt()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();
        var handler = new ApplyManagedFiscalProfilesCommandHandler(profiles.Object, Follower(), Fakes.Options(Fakes.Options()), NullLogger<ApplyManagedFiscalProfilesCommandHandler>.Instance);

        // Act
        var applied = await handler.Handle(new ApplyManagedFiscalProfilesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal((1, "48"), (applied, stored.TaxRegimeCode));
    }

    // ─── PUT api/Issuer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateIssuer_WithHistory_IgnoresTheRegimeAndTheManagedResponsibilitiesOfTheRequest()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible(withholdingAgent: false);

        // Act: the screen sends no regime; an old client could still send 49 and O-23.
        await UpdateHandler().Handle(Update("49", "O-23", "O-13"), CancellationToken.None);

        // Assert
        Assert.Equal(("48", "O-13"), (stored.TaxRegimeCode, string.Join(',', stored.TaxResponsibilities)));
    }

    [Fact]
    public async Task UpdateIssuer_NoResponsibilityMarked_KeepsTheCodeOfNone()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible(withholdingAgent: false);

        // Act
        await UpdateHandler().Handle(Update(null), CancellationToken.None);

        // Assert
        Assert.Equal("R-99-PN", string.Join(',', stored.TaxResponsibilities));
    }

    [Fact]
    public async Task UpdateIssuer_WithoutRegimeNorHistory_KeepsTheRegimeItHad()
    {
        // Arrange
        stored = NotResponsibleIssuer();

        // Act
        await UpdateHandler().Handle(Update(null, "O-13"), CancellationToken.None);

        // Assert
        Assert.Equal(("49", "O-13"), (stored.TaxRegimeCode, string.Join(',', stored.TaxResponsibilities)));
    }

    [Fact]
    public async Task UpdateIssuer_ProvisionedIssuer_SendsTheDataToTheProviderFirst()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();

        // Act
        await UpdateHandler().Handle(Update(null, "O-13"), CancellationToken.None);

        // Assert
        provisioning.Verify(x => x.UpdateIssuerAsync(It.Is<ProviderIssuer>(i => i.TaxRegimeCode == "48" && i.TaxResponsibilities.Contains("O-13") && i.TaxResponsibilities.Contains("O-23")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateIssuer_ProviderRefuses_ChangesNothing()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        provisioning.Setup(x => x.UpdateIssuerAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ThrowsAsync(Refusal());

        // Act
        await Assert.ThrowsAsync<CodeDesignPlusException>(() => UpdateHandler().Handle(Update(null, "O-13"), CancellationToken.None));

        // Assert
        issuers.Verify(x => x.UpdateWithRetryAsync(It.IsAny<Func<Task<IssuerAggregate>>>(), It.IsAny<Func<IssuerAggregate, bool>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── POST api/Issuer ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterIssuer_WithHistory_TakesItsRegimeAndResponsibilities()
    {
        // Arrange
        profile = Responsible();

        // Act
        await RegisterHandler().Handle(Register("49", "R-99-PN"), CancellationToken.None);

        // Assert
        Assert.Equal(("48", "O-23"), (stored!.TaxRegimeCode, string.Join(',', stored.TaxResponsibilities)));
    }

    [Fact]
    public async Task RegisterIssuer_WithoutRegimeNorHistory_ThrowsTaxRegimeIsRequired()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => RegisterHandler().Handle(Register(null), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.TaxRegimeIsRequired.GetCode(), exception.Code);
    }

    // ─── GET api/Issuer and GET api/Issuer/options ──────────────────────────────

    [Fact]
    public async Task GetIssuer_IssuerBehindTheHistory_SaysWhatEachSideHas()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        profile = Responsible();

        // Act
        var issuer = await GetHandler().Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        var mismatch = issuer!.FiscalProfileMismatch!;

        Assert.Equal(("49", "R-99-PN", "48", "O-23"), (mismatch.Issuer.TaxRegimeCode, string.Join(',', mismatch.Issuer.TaxResponsibilities), mismatch.FiscalProfile.TaxRegimeCode, string.Join(',', mismatch.FiscalProfile.TaxResponsibilities)));
    }

    [Fact]
    public async Task GetIssuer_IssuerAligned_HasNoMismatch()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        stored.FollowFiscalData("48", ["O-23"], TestData.SystemUser);
        profile = Responsible();

        // Act
        var issuer = await GetHandler().Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Null(issuer!.FiscalProfileMismatch);
    }

    [Fact]
    public async Task GetIssuer_RefusedByTheProvider_SaysWhyAndCannotIssue()
    {
        // Arrange
        stored = NotResponsibleIssuer();
        stored.RecordFiscalDataRejection("318", TestData.SystemUser);

        // Act
        var issuer = await GetHandler().Handle(new GetIssuerQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(("318", false), (issuer!.FiscalDataRejectionCode, issuer.CanIssue));
    }

    [Fact]
    public async Task GetIssuerOptions_WithHistory_GivesTheManagedCodesAndWhatRulesToday()
    {
        // Arrange
        profile = Responsible();
        var handler = new GetIssuerOptionsQueryHandler(Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Options(Fakes.Options()), profiles.Object, Fakes.Clock());

        // Act
        var options = await handler.Handle(new GetIssuerOptionsQuery("CO"), CancellationToken.None);

        // Assert
        Assert.Equal(("O-23", "48", "O-23"), (string.Join(',', options.FiscalProfileResponsibilities), options.FiscalProfile!.TaxRegimeCode, string.Join(',', options.FiscalProfile.TaxResponsibilities)));
    }

    [Fact]
    public async Task GetIssuerOptions_WithoutHistory_HasNoFiscalProfile()
    {
        // Arrange
        var handler = new GetIssuerOptionsQueryHandler(Mock.Of<ITestProviderGrantRepository>(), user.Object, Fakes.Options(Fakes.Options()), profiles.Object, Fakes.Clock());

        // Act
        var options = await handler.Handle(new GetIssuerOptionsQuery("CO"), CancellationToken.None);

        // Assert
        Assert.Equal((0, null), (options.FiscalProfileResponsibilities.Count, options.FiscalProfile));
    }
}
