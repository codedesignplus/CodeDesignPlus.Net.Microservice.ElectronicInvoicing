namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Helpers;

/// <summary>
/// Decides whether the tests that send documents to a real provider run.
/// </summary>
/// <remarks>
/// A token in the environment is not a wish to send: a machine keeps it for other uses, and a plain <c>dotnet test</c> of
/// the solution would send documents to the provider without anybody asking (it happened on 2026-11-19). So the tests
/// run only when the token is there <b>and</b> <see cref="OptInVariable"/> says <see cref="OptInValue"/>.
/// </remarks>
public static class ProviderSandbox
{
    /// <summary>
    /// The variable that asks for the tests against a real provider.
    /// </summary>
    public const string OptInVariable = "RUN_PROVIDER_SANDBOX_TESTS";

    /// <summary>
    /// The only value of <see cref="OptInVariable"/> that asks for them.
    /// </summary>
    public const string OptInValue = "1";

    /// <summary>
    /// Why a test against a provider is skipped in this environment.
    /// </summary>
    /// <param name="tokenVariable">The variable with the token of the provider.</param>
    /// <returns>The reason, or null when the test runs.</returns>
    public static string? SkipReason(string tokenVariable) => SkipReason(tokenVariable, Environment.GetEnvironmentVariable(OptInVariable), Environment.GetEnvironmentVariable(tokenVariable));

    /// <summary>
    /// Why a test against a provider is skipped, given what the environment says.
    /// </summary>
    /// <param name="tokenVariable">The variable with the token of the provider.</param>
    /// <param name="optIn">The value of <see cref="OptInVariable"/>.</param>
    /// <param name="token">The token of the provider.</param>
    /// <returns>The reason, or null when the test runs.</returns>
    public static string? SkipReason(string tokenVariable, string? optIn, string? token)
    {
        if (!string.Equals(optIn, OptInValue, StringComparison.Ordinal))
        {
            return $"{OptInVariable} is not {OptInValue}: the tests that send documents to a provider run only when asked for.";
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return $"{tokenVariable} is not set: the provider is not reachable from here.";
        }

        return null;
    }
}
