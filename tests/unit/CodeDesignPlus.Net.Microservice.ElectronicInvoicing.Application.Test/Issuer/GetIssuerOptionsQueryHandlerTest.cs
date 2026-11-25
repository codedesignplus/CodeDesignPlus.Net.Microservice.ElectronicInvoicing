using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuerOptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Helpers;
using AppErrors = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Errors;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Test.Issuer;

/// <summary>
/// The form of an issuer offers the lists of the configuration of its country, never a list written in the code, and only
/// the providers the issuer of the scope may choose.
/// </summary>
public class GetIssuerOptionsQueryHandlerTest
{
    private readonly Mock<ITestProviderGrantRepository> grants = new();
    private readonly Mock<IUserContext> user = new();

    public GetIssuerOptionsQueryHandlerTest()
    {
        user.Setup(x => x.Tenant).Returns(TestData.Tenant);
        user.Setup(x => x.IdUser).Returns(TestData.User);
    }

    private GetIssuerOptionsQueryHandler Handler(ElectronicInvoicingOptions options) => new(grants.Object, user.Object, Fakes.Options(options));

    private void Grant(bool granted)
    {
        var grant = TestProviderGrantAggregate.Grant(TestData.Tenant, TestData.SystemUser, TestData.Now);

        if (!granted)
        {
            grant.Revoke(TestData.SystemUser, TestData.Now);
        }

        grants.Setup(x => x.FindAsync<TestProviderGrantAggregate>(grant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(grant);
    }

    [Fact]
    public async Task Handle_WithoutCountry_AnswersTheListsOfTheConfigurationOfTheOnlyCountry()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.Countries["CO"].TaxRegimes = [new() { Code = "77", Name = "Régimen de la configuración" }];

        // Act
        var answer = await Handler(options).Handle(new GetIssuerOptionsQuery(), CancellationToken.None);

        // Assert
        Assert.Equal([new IssuerOptionDto("77", "Régimen de la configuración")], answer.TaxRegimes);
    }

    [Fact]
    public async Task Handle_Country_AnswersItsPersonTypesAndResponsibilitiesInTheirOrder()
    {
        // Act
        var answer = await Handler(Fakes.Options()).Handle(new GetIssuerOptionsQuery("co"), CancellationToken.None);

        // Assert
        Assert.Equal("CO|1,2|O-13,R-99-PN", $"{answer.Country}|{string.Join(',', answer.PersonTypes.Select(x => x.Code))}|{string.Join(',', answer.TaxResponsibilities.Select(x => x.Code))}");
    }

    [Fact]
    public async Task Handle_Call_AnswersTheVersionOfTheMandateOfTheConfiguration()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.MandateVersion = "2.1";

        // Act
        var answer = await Handler(options).Handle(new GetIssuerOptionsQuery("CO"), CancellationToken.None);

        // Assert
        Assert.Equal("2.1", answer.MandateVersion);
    }

    [Fact]
    public async Task Handle_TenantWithoutPermission_OffersOnlyTheProvidersOfTheRouting()
    {
        // Arrange
        Grant(false);

        // Act
        var answer = await Handler(Fakes.TestProviderOptions()).Handle(new GetIssuerOptionsQuery("CO"), CancellationToken.None);

        // Assert
        Assert.Equal("Other|", $"{string.Join(',', answer.AvailableProviders)}|{string.Join(',', answer.TestProviders)}");
    }

    [Fact]
    public async Task Handle_TenantWithPermission_OffersTheTestProviderAndMarksIt()
    {
        // Arrange
        Grant(true);

        // Act
        var answer = await Handler(Fakes.TestProviderOptions()).Handle(new GetIssuerOptionsQuery("CO"), CancellationToken.None);

        // Assert
        Assert.Equal("Other,Simulated|Simulated", $"{string.Join(',', answer.AvailableProviders)}|{string.Join(',', answer.TestProviders)}");
    }

    [Fact]
    public async Task Handle_PlatformIssuerWithATenantPermission_NeverOffersTheTestProvider()
    {
        // Arrange
        Grant(true);

        // Act
        var answer = await Handler(Fakes.TestProviderOptions()).Handle(new GetIssuerOptionsQuery("CO", PlatformIssuer: true), CancellationToken.None);

        // Assert
        Assert.Equal(["Other"], answer.AvailableProviders);
    }

    [Fact]
    public async Task Handle_CountryWithoutElectronicInvoicing_ThrowsCountryWithoutElectronicInvoicing()
    {
        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Handler(Fakes.Options()).Handle(new GetIssuerOptionsQuery("PE"), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.CountryWithoutElectronicInvoicing.GetCode(), exception.Code);
    }

    [Fact]
    public async Task Handle_WithoutCountryAndSeveralInTheRouting_ThrowsIssuerCountryIsRequired()
    {
        // Arrange
        var options = Fakes.Options();

        options.Routing["MX"] = ["Other"];

        // Act
        var exception = await Assert.ThrowsAsync<CodeDesignPlusException>(() => Handler(options).Handle(new GetIssuerOptionsQuery(), CancellationToken.None));

        // Assert
        Assert.Equal(AppErrors.IssuerCountryIsRequired.GetCode(), exception.Code);
    }
}

/// <summary>
/// The lists of the form are validated on start: a deployment whose configuration lacks them does not start.
/// </summary>
public class IssuerSetupOptionsTest
{
    private static List<ValidationResult> Problems(ElectronicInvoicingOptions options) => [.. options.Validate(new ValidationContext(options))];

    [Fact]
    public void Validate_CountryOfTheRoutingWithoutItsLists_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.Routing["MX"] = ["Other"];

        // Act
        var problems = Problems(options);

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("IssuerOptions:Countries:MX"));
    }

    [Fact]
    public void Validate_WithoutMandateVersion_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.MandateVersion = " ";

        // Act
        var problems = Problems(options);

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("IssuerOptions:MandateVersion"));
    }

    [Fact]
    public void Validate_EmptyList_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.Countries["CO"].PersonTypes = [];

        // Act
        var problems = Problems(options);

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("IssuerOptions:Countries:CO:PersonTypes"));
    }

    [Fact]
    public void Validate_RepeatedCode_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.Countries["CO"].TaxResponsibilities = [new() { Code = "O-13", Name = "Gran contribuyente" }, new() { Code = "o-13", Name = "Otra" }];

        // Act
        var problems = Problems(options);

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("TaxResponsibilities cannot repeat a code"));
    }

    [Fact]
    public void Validate_EntryWithoutName_SaysSo()
    {
        // Arrange
        var options = Fakes.Options();

        options.IssuerOptions.Countries["CO"].TaxRegimes = [new() { Code = "48", Name = "" }];

        // Act
        var problems = Problems(options);

        // Assert
        Assert.Contains(problems, x => x.ErrorMessage!.Contains("TaxRegimes cannot carry an entry without its code or its name"));
    }
}
