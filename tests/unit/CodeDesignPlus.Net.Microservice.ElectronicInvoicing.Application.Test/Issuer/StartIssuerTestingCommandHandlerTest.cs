using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

public class StartIssuerTestingCommandHandlerTest
{
    private const string TestSetId = "a70562e0-631e-4ceb-aa65-36887b57dc17";

    private readonly Mock<IIssuerRepository> issuers = new();
    private readonly Mock<IElectronicInvoiceProviderFactory> factory = new();
    private readonly Mock<IProviderTestSetRunner> runner = new();
    private readonly Mock<IProviderIssuerResolver> resolver = new();
    private readonly Mock<IUserContext> user = new();

    public StartIssuerTestingCommandHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
        runner.Setup(x => x.Capabilities).Returns(Fakes.Capabilities());
        factory.Setup(x => x.Get("Simulated")).Returns(runner.Object);
        resolver.Setup(x => x.ResolveAsync(It.IsAny<IssuerAggregate>(), It.IsAny<CancellationToken>())).ReturnsAsync((IssuerAggregate issuer, CancellationToken _) => ProviderIssuer.From(issuer, new Dictionary<string, string>()));
        issuers.EmulateOptimisticUpdate<IIssuerRepository, IssuerAggregate>();
    }

    private StartIssuerTestingCommandHandler Handler() => new(issuers.Object, factory.Object, resolver.Object, Mock.Of<IPubSub>(), user.Object, NullLogger<StartIssuerTestingCommandHandler>.Instance);

    private IssuerAggregate Stored(IssuerAggregate issuer)
    {
        issuers.Setup(x => x.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(TestData.Tenant), It.IsAny<CancellationToken>())).ReturnsAsync(issuer);

        return issuer;
    }

    private static IssuerAggregate WithMandate()
    {
        var issuer = TestData.RegisteredIssuer();

        issuer.AcceptMandate(TestData.Mandate(), TestData.User);

        return issuer;
    }

    private void Runs(ProviderDocumentStatus status)
    {
        factory.Setup(x => x.FindTestSetRunner("Simulated")).Returns(runner.Object);
        runner.Setup(x => x.RunTestSetAsync(It.IsAny<ProviderIssuer>(), It.IsAny<ElectronicDocumentType>(), TestSetId, It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderTestSet(status, status.ToString()));
    }

    [Fact]
    public async Task Handle_ProviderThatRunsTheTestSetAndAcceptsIt_StartsTheTests()
    {
        // Arrange
        var issuer = Stored(WithMandate());

        Runs(ProviderDocumentStatus.Accepted);

        // Act
        await Handler().Handle(new StartIssuerTestingCommand(TestSetId), CancellationToken.None);

        // Assert
        Assert.Equal((IssuerStatus.Testing, TestSetId), (issuer.Status, issuer.TestSetId));
    }

    [Fact]
    public async Task Handle_WithoutType_AsksForTheTestSetOfTheInvoices()
    {
        // Arrange
        Stored(WithMandate());
        Runs(ProviderDocumentStatus.Accepted);

        // Act
        await Handler().Handle(new StartIssuerTestingCommand(TestSetId), CancellationToken.None);

        // Assert
        runner.Verify(x => x.RunTestSetAsync(It.IsAny<ProviderIssuer>(), ElectronicDocumentType.Invoice, TestSetId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProviderRejectsTheTestSet_ThrowsTestSetRejectedAndKeepsTheIssuer()
    {
        // Arrange
        var issuer = Stored(WithMandate());

        Runs(ProviderDocumentStatus.Rejected);

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Handler().Handle(new StartIssuerTestingCommand(TestSetId), CancellationToken.None));

        // Assert
        Assert.Equal((AppErrors.TestSetRejected.GetCode(), IssuerStatus.Draft), (exception.Code, issuer.Status));
    }

    [Fact]
    public async Task Handle_EnabledIssuerThatEnablesThePointOfSale_StaysEnabled()
    {
        // Arrange
        var issuer = Stored(TestData.EnabledIssuer());

        Runs(ProviderDocumentStatus.Accepted);

        // Act
        await Handler().Handle(new StartIssuerTestingCommand(TestSetId, ElectronicDocumentType.PosTicket), CancellationToken.None);

        // Assert
        Assert.Equal(IssuerStatus.Enabled, issuer.Status);
    }

    [Fact]
    public async Task Handle_ProviderWhoseSetGoesThroughThisService_StartsTheTestsWithoutCallingIt()
    {
        // Arrange
        var issuer = Stored(WithMandate());

        // Act
        await Handler().Handle(new StartIssuerTestingCommand(TestSetId), CancellationToken.None);

        // Assert
        Assert.Equal((IssuerStatus.Testing, 0), (issuer.Status, runner.Invocations.Count(x => x.Method.Name == nameof(IProviderTestSetRunner.RunTestSetAsync))));
    }
}
