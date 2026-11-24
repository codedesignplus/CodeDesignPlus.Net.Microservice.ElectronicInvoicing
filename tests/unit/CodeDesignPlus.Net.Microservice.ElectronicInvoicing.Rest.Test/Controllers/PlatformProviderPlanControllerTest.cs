using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Test.Controllers;

/// <summary>
/// The month of the provider's plan, with its overage and its estimated cost, for whoever runs the platform.
/// </summary>
public class PlatformProviderPlanControllerTest
{
    private readonly Mock<IMediator> mediator = new();

    private static ProviderPlanMonth Month() => new("Alanube", new LocalDate(2026, 11, 1), Instant.FromUtc(2026, 11, 1, 5, 0), Instant.FromUtc(2026, 12, 1, 5, 0), 3_000, 3_500, 500, 700_000, "COP");

    [Fact]
    public async Task GetMonth_DayOfAMonth_AsksForThatMonth()
    {
        // Arrange
        var month = Month();

        mediator.Setup(x => x.Send(It.Is<GetProviderPlanMonthQuery>(q => q.Month == new LocalDate(2026, 11, 17)), It.IsAny<CancellationToken>())).ReturnsAsync(month);

        // Act
        var result = await new PlatformProviderPlanController(mediator.Object).GetMonth("2026-11-17", CancellationToken.None);

        // Assert
        Assert.Same(month, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task GetMonth_WithoutMonth_AsksForTheCurrentOne()
    {
        // Arrange
        mediator.Setup(x => x.Send(It.Is<GetProviderPlanMonthQuery>(q => q.Month == null), It.IsAny<CancellationToken>())).ReturnsAsync(Month());

        // Act
        var result = await new PlatformProviderPlanController(mediator.Object).GetMonth(null, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetMonth_MonthTheContractDoesNotCover_AnswersNoContent()
    {
        // Act
        var result = await new PlatformProviderPlanController(mediator.Object).GetMonth("2020-01-01", CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetMonth_TextThatIsNotADate_ThrowsInvalidDate()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => new PlatformProviderPlanController(mediator.Object).GetMonth("2026-11", CancellationToken.None));

        // Assert
        Assert.Equal(Infrastructure.Errors.InvalidDate.GetCode(), exception.Code);
    }
}
