using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Exceptions.Guards;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Vault.Abstractions;
using CodeDesignPlus.Net.Vault.Abstractions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Vault;

/// <summary>
/// Encrypts the credentials of the issuers with the Vault's Transit engine, as ms-payments keeps the keys of the
/// collection accounts.
/// </summary>
/// <remarks>
/// The context is the one configured in <c>Vault:Transit:SecretContexts:electronic_issuer</c> joined to the tenant id:
/// a ciphertext copied from one issuer to another does not decrypt. Every issuer is encrypted with the same key,
/// <c>ElectronicInvoicing:SecretsKeyName</c>, which the SDK creates derived on first use; a key per encryption would
/// leave a key per issuer in the Vault, orphaned when the tenant is purged. The credentials of an issuer go in one batch,
/// in the order of their names.
/// <para>
/// A Vault that does not answer is reported as <see cref="Errors.VaultUnavailable"/>, which the entrypoints answer as
/// unavailable so the caller retries; nothing falls back to secrets in clear.
/// </para>
/// </remarks>
/// <param name="transit">The Transit engine of the Vault.</param>
/// <param name="options">The options of the Vault.</param>
/// <param name="invoicingOptions">The options of the service.</param>
/// <param name="logger">The logger.</param>
public sealed class VaultIssuerSecretCipher(IVaultTransit transit, IOptions<VaultOptions> options, IOptions<ElectronicInvoicingOptions> invoicingOptions, ILogger<VaultIssuerSecretCipher> logger) : IIssuerSecretCipher
{
    /// <summary>
    /// Name of the secret context in <c>Vault:Transit:SecretContexts</c>.
    /// </summary>
    public const string SecretContextName = "electronic_issuer";

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> SealAsync(Guid tenant, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var names = secrets.Keys.Order(StringComparer.Ordinal).ToList();

        var ciphertexts = await CallVaultAsync(() => transit.EncryptAsync(invoicingOptions.Value.SecretsKeyName, [.. names.Select(x => secrets[x])], ContextOf(tenant)), tenant);

        return names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => ciphertexts[x.index]);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> OpenAsync(Guid tenant, IReadOnlyDictionary<string, string> ciphertexts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ciphertexts);

        var names = ciphertexts.Keys.Order(StringComparer.Ordinal).ToList();

        var plain = await CallVaultAsync(() => transit.DecryptAsync(invoicingOptions.Value.SecretsKeyName, [.. names.Select(x => ciphertexts[x])], ContextOf(tenant)), tenant);

        return names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => plain[x.index]);
    }

    private async Task<T> CallVaultAsync<T>(Func<Task<T>> call, Guid tenant)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not CodeDesignPlusException)
        {
            // Only the type and the tenant: the message of a Vault error may echo what it was given.
            logger.LogWarning("The Vault did not encrypt or decrypt the credentials of the issuer of tenant {Tenant}: {Error}", tenant, exception.GetType().Name);

            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.VaultUnavailable.GetCode(), Errors.VaultUnavailable.GetMessage());
        }
    }

    private string ContextOf(Guid tenant)
    {
        var found = options.Value.Transit.SecretContexts.TryGetValue(SecretContextName, out var context);

        InfrastructureGuard.IsFalse(found && !string.IsNullOrWhiteSpace(context), Errors.SecretContextNotConfigured);

        return $"{context}:{tenant}";
    }
}
