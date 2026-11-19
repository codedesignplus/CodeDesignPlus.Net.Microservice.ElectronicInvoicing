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

    [Theory]
    [InlineData(typeof(AlanubeSandboxTest))]
    [InlineData(typeof(AliaddoSandboxTest))]
    public void SandboxTests_EveryTest_AsksTheSandboxWhetherItRuns(Type type)
    {
        // Arrange: a plain [Fact] in these classes would send documents on every run.
        var gated = new[] { typeof(SandboxFactAttribute), typeof(AliaddoSandboxFactAttribute), typeof(AliaddoSandboxTheoryAttribute) };

        // Act
        var ungated = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes<FactAttribute>().Any(a => !gated.Contains(a.GetType())))
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
        var skips = new[] { new SandboxFactAttribute().Skip, new AliaddoSandboxFactAttribute().Skip, new AliaddoSandboxTheoryAttribute().Skip };

        // Assert
        Assert.DoesNotContain(skips, string.IsNullOrEmpty);
    }
}
