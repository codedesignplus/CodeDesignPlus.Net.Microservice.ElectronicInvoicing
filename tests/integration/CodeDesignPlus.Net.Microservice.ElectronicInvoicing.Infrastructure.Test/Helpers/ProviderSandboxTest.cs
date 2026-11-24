using System.Reflection;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Helpers;

/// <summary>
/// The tests against a real provider do not run just because its token is on the machine.
/// </summary>
public class ProviderSandboxTest
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void SkipReason_TokenWithoutTheOptIn_Skips(string? optIn)
    {
        // Act
        var reason = ProviderSandbox.SkipReason("PROVIDER_TOKEN", optIn, "a-token");

        // Assert
        Assert.Contains(ProviderSandbox.OptInVariable, reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void SkipReason_OptInWithoutTheToken_Skips(string? token)
    {
        // Act
        var reason = ProviderSandbox.SkipReason("PROVIDER_TOKEN", ProviderSandbox.OptInValue, token);

        // Assert
        Assert.Contains("PROVIDER_TOKEN", reason);
    }

    [Fact]
    public void SkipReason_OptInAndToken_Runs()
    {
        // Act
        var reason = ProviderSandbox.SkipReason("PROVIDER_TOKEN", ProviderSandbox.OptInValue, "a-token");

        // Assert
        Assert.Null(reason);
    }

    [Fact]
    public void SandboxTests_EveryTest_AsksTheSandboxWhetherItRuns()
    {
        // Arrange: a plain [Fact] in this class would send documents on every run.
        var type = typeof(AlanubeSandboxTest);

        // Act
        var ungated = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes<FactAttribute>().Any(a => a.GetType() != typeof(SandboxFactAttribute)))
            .Select(x => x.Name)
            .ToList();

        // Assert
        Assert.Empty(ungated);
    }

    [Fact]
    public void SandboxAttributes_WithoutTheOptIn_AreSkipped()
    {
        // Arrange: this run did not ask for the sandbox, whatever tokens the machine has.
        if (Environment.GetEnvironmentVariable(ProviderSandbox.OptInVariable) == ProviderSandbox.OptInValue)
        {
            return;
        }

        // Act
        var skip = new SandboxFactAttribute().Skip;

        // Assert
        Assert.False(string.IsNullOrEmpty(skip));
    }
}
