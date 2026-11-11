using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.NumberingRange;

public class RegisterNumberingRangeCommandHandlerTest
{
    private readonly Mock<INumberingRangeRepository> ranges = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IElectronicInvoiceProvider> provider = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly RegisterNumberingRangeCommandHandler handler;

    public RegisterNumberingRangeCommandHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(provider.Object);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        handler = new RegisterNumberingRangeCommandHandler(ranges.Object, issuers.Object, factory.Object, user.Object, pubsub.Object);
    }

    private static RegisterNumberingRangeCommand Command() => new(ElectronicDocumentType.Invoice, "18760000001", "technical-key", "FAC", 1, 5000, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    [Fact]
    public async Task Handle_NewRange_PublishesItsResolution()
    {
        // Arrange
        ranges.Setup(x => x.CreateIfAbsentAsync(It.IsAny<NumberingRangeAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var id = await handler.Handle(Command(), CancellationToken.None);

        // Assert
        var registered = Assert.IsType<NumberingRangeRegisteredDomainEvent>(pubsub.Published().Single());

        Assert.Equal((id, TestData.Tenant, issuer.Id, "FAC", "18760000001", 1L, 5000L), (registered.AggregateId, registered.Tenant, registered.IssuerId, registered.Prefix, registered.ResolutionNumber, registered.From, registered.To));
    }

    [Fact]
    public async Task Handle_RangeSavedAtOnceByAnotherRequest_PublishesNothing()
    {
        // Arrange
        ranges.Setup(x => x.CreateIfAbsentAsync(It.IsAny<NumberingRangeAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        await handler.Handle(Command(), CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }

    [Fact]
    public async Task Handle_RangeAlreadyRegistered_PublishesNothing()
    {
        // Arrange
        ranges.Setup(x => x.ExistsAsync<NumberingRangeAggregate>(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        await handler.Handle(Command(), CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }
}

public class NumberingRangeValidatorsTest
{
    [Theory]
    [InlineData("FAC-")]
    [InlineData("FACTU")]
    [InlineData("")]
    [InlineData("F.C")]
    public void RegisterValidator_PrefixThatCannotBeOne_IsRefusedWithItsCode(string prefix)
    {
        // Arrange
        var command = new RegisterNumberingRangeCommand(ElectronicDocumentType.Invoice, "18760000001", null, prefix, 1, 10, new LocalDate(2026, 1, 1), new LocalDate(2026, 12, 31));

        // Act
        var result = new RegisterNumberingRangeValidator().Validate(command);

        // Assert
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RegisterNumberingRangeCommand.Prefix) && x.ErrorCode == DomainErrors.PrefixIsInvalid.GetCode());
    }

    [Fact]
    public void RegisterValidator_PrefixOfFourLettersAndDigits_IsValid()
    {
        // Arrange
        var command = new RegisterNumberingRangeCommand(ElectronicDocumentType.Invoice, "18760000001", null, "FE01", 1, 10, new LocalDate(2026, 1, 1), new LocalDate(2026, 12, 31));

        // Act
        var result = new RegisterNumberingRangeValidator().Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SyncValidator_PrefixWithAHyphen_IsRefusedWithItsCode()
    {
        // Act
        var result = new SyncNumberingRangeValidator().Validate(new SyncNumberingRangeCommand(ElectronicDocumentType.Invoice, "FE-"));

        // Assert
        Assert.Contains(result.Errors, x => x.ErrorCode == DomainErrors.PrefixIsInvalid.GetCode());
    }
}

public class SyncNumberingRangeCommandHandlerTest
{
    private readonly Mock<INumberingRangeRepository> ranges = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IIssuerProvisioningProvider> provider = new();
    private readonly Mock<IProviderIssuerResolver> resolver = new();
    private readonly Mock<IUserContext> user = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly SyncNumberingRangeCommandHandler handler;

    public SyncNumberingRangeCommandHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        provider.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.FindProvisioning("Simulated")).Returns(provider.Object);
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);
        ranges.EmulateOptimisticUpdate<INumberingRangeRepository, NumberingRangeAggregate>();

        handler = new SyncNumberingRangeCommandHandler(ranges.Object, issuers.Object, factory.Object, resolver.Object, user.Object, pubsub.Object);
    }

    private void ProviderSays(long to) => provider.Setup(x => x.GetNumberingRangesAsync(It.IsAny<ProviderIssuer>(), It.IsAny<CancellationToken>())).ReturnsAsync([new ProviderNumberingRange("technical-key-of-test", "SETP", "18760000001", 990000000, to, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31))]);

    [Fact]
    public async Task Handle_RangeNotYetHere_CreatesItAndPublishesItsResolution()
    {
        // Arrange
        ProviderSays(995000000);

        // Act
        await handler.Handle(new SyncNumberingRangeCommand(ElectronicDocumentType.Invoice, "SETP"), CancellationToken.None);

        // Assert
        Assert.Equal(995000000L, Assert.IsType<NumberingRangeRegisteredDomainEvent>(pubsub.Published().Single()).To);
    }

    [Fact]
    public async Task Handle_ProviderExtendedTheRange_PublishesTheNewResolution()
    {
        // Arrange
        var stored = Stored();

        ProviderSays(999000000);
        ranges.Setup(x => x.FindAsync<NumberingRangeAggregate>(stored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        // Act
        await handler.Handle(new SyncNumberingRangeCommand(ElectronicDocumentType.Invoice, "SETP"), CancellationToken.None);

        // Assert
        Assert.Equal(999000000L, Assert.IsType<NumberingRangeRegisteredDomainEvent>(pubsub.Published().Single()).To);
    }

    [Fact]
    public async Task Handle_ProviderHasItAsItWas_PublishesNothing()
    {
        // Arrange
        var stored = Stored();

        ProviderSays(995000000);
        ranges.Setup(x => x.FindAsync<NumberingRangeAggregate>(stored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        // Act
        await handler.Handle(new SyncNumberingRangeCommand(ElectronicDocumentType.Invoice, "SETP"), CancellationToken.None);

        // Assert
        Assert.Empty(pubsub.Published());
    }

    /// <summary>
    /// A range already saved: with a version, and without the events of its creation.
    /// </summary>
    private NumberingRangeAggregate Stored()
    {
        var range = TestData.Range(issuer);

        range.GetAndClearEvents();
        typeof(NumberingRangeAggregate).GetProperty(nameof(NumberingRangeAggregate.Version))!.SetValue(range, 1L);

        return range;
    }
}

public class WatchNumberingRangesCommandHandlerTest
{
    private readonly Mock<INumberingRangeRepository> ranges = new();
    private readonly Mock<IElectronicDocumentRepository> documents = new();
    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IPubSub> pubsub = new();
    private readonly IssuerAggregate issuer = TestData.EnabledIssuer();
    private readonly NumberingRangeAggregate range;
    private readonly WatchNumberingRangesCommandHandler handler;

    public WatchNumberingRangesCommandHandlerTest()
    {
        range = TestData.Range(issuer);
        range.GetAndClearEvents();

        ranges.Setup(x => x.FindWatchedAsync(It.IsAny<LocalDate>(), It.IsAny<CancellationToken>())).ReturnsAsync([range]);
        ranges.Setup(x => x.FindAsync<NumberingRangeAggregate>(range.Id, It.IsAny<CancellationToken>())).ReturnsAsync(range);
        ranges.EmulateOptimisticUpdate<INumberingRangeRepository, NumberingRangeAggregate>();
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(issuer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        handler = new WatchNumberingRangesCommandHandler(ranges.Object, documents.Object, issuers.Object, pubsub.Object, Fakes.Clock(), Fakes.Options(Fakes.Options()), NullLogger<WatchNumberingRangesCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_CallerNumberedAlmostTheWholeRange_AnnouncesItIsRunningOut()
    {
        // Arrange: no number reserved here, but the callers already reached the 994999000th of 995000000.
        documents.Setup(x => x.FindHighestConsecutiveAsync(issuer.Id, ElectronicDocumentType.Invoice, "SETP", 990000000, 995000000, It.IsAny<CancellationToken>())).ReturnsAsync(994999000L);

        // Act
        await handler.Handle(new WatchNumberingRangesCommand(), CancellationToken.None);

        // Assert
        var alert = Assert.IsType<NumberingRangeRunningOutDomainEvent>(pubsub.Published().Single());

        Assert.Equal((NumberingRangeAlert.Numbers, 1000L), (alert.Alert, alert.Remaining));
    }

    [Fact]
    public async Task Handle_NoDocumentInTheRange_AnnouncesNothing()
    {
        // Act
        var raised = await handler.Handle(new WatchNumberingRangesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(0, raised);
    }
}
