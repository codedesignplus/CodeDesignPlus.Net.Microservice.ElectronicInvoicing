using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Quota;

public class WatchProviderPlanCommandHandlerTest
{
    [Fact]
    public async Task Handle_IssuersOfSeveralProviders_CountsOnlyTheDocumentsOfTheIssuersOfThePlansProvider()
    {
        // Arrange
        var options = Fakes.Options();
        var plan = options.ProviderPlan;
        var ofThePlan = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var documents = new Mock<IElectronicDocumentRepository>();
        var issuers = new Mock<IIssuerRepository>();
        var usages = new Mock<IProviderPlanUsageRepository>();

        issuers.Setup(x => x.FindIdsByProviderAsync(plan.Provider, It.IsAny<CancellationToken>())).ReturnsAsync(ofThePlan);
        documents.Setup(x => x.CountFirstSentBetweenAsync(plan.From, plan.Until, ofThePlan, It.IsAny<CancellationToken>())).ReturnsAsync(7);
        usages.EmulateOptimisticUpdate<IProviderPlanUsageRepository, ProviderPlanUsageAggregate>();

        var handler = new WatchProviderPlanCommandHandler(documents.Object, issuers.Object, usages.Object, Mock.Of<IPubSub>(), Fakes.Clock(), Fakes.Options(options));

        // Act
        var used = await handler.Handle(new WatchProviderPlanCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(7, used);
    }
}
