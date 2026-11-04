using System.Collections.Concurrent;
using System.Numerics;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;

/// <summary>
/// Encrypts and decrypts the credentials of the issuers that bring their own, with the Vault. Only ciphertext is
/// stored, and the plain values live in memory for the operation that needs them.
/// </summary>
public interface IIssuerSecretCipher
{
    /// <summary>
    /// Encrypts the credentials of an issuer, bound to its tenant.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer; a ciphertext of one tenant cannot be decrypted as another's.</param>
    /// <param name="secrets">The credentials in clear, by name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ciphertext of each credential, by the same name.</returns>
    Task<Dictionary<string, string>> SealAsync(Guid tenant, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken);

    /// <summary>
    /// Decrypts the credentials of an issuer. Never log the result.
    /// </summary>
    /// <param name="tenant">The tenant of the issuer.</param>
    /// <param name="ciphertexts">The ciphertext of each credential, by name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The credentials in clear, by the same name.</returns>
    Task<Dictionary<string, string>> OpenAsync(Guid tenant, IReadOnlyDictionary<string, string> ciphertexts, CancellationToken cancellationToken);
}

/// <summary>
/// Turns an issuer into what an adapter needs, opening its credentials when it brings its own.
/// </summary>
public interface IProviderIssuerResolver
{
    /// <summary>
    /// The issuer for the adapter.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issuer with its credentials opened.</returns>
    Task<ProviderIssuer> ResolveAsync(IssuerAggregate issuer, CancellationToken cancellationToken);
}

/// <summary>
/// Opens the credentials of an issuer once per version and keeps them in this process's memory for
/// <see cref="ElectronicInvoicingOptions.SecretsCacheLifetime"/>: a replacement raises the version and is noticed.
/// </summary>
/// <param name="cipher">The cipher of the Vault.</param>
/// <param name="cache">The memory of this process.</param>
/// <param name="options">The options of the service.</param>
public sealed class ProviderIssuerResolver(IIssuerSecretCipher cipher, IMemoryCache cache, IOptions<ElectronicInvoicingOptions> options) : IProviderIssuerResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();

    private static readonly ConcurrentDictionary<Guid, long> Versions = new();

    /// <inheritdoc/>
    public async Task<ProviderIssuer> ResolveAsync(IssuerAggregate issuer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        if (issuer.Credentials is null || issuer.Credentials.Ciphertexts.Count == 0)
        {
            return ProviderIssuer.From(issuer, NoSecrets);
        }

        var version = issuer.Credentials.Version;
        var key = $"issuer-secrets:{issuer.Id}:{version}";

        if (!cache.TryGetValue(key, out Dictionary<string, string>? secrets) || secrets is null)
        {
            secrets = await cipher.OpenAsync(issuer.Tenant, issuer.Credentials.Ciphertexts, cancellationToken);

            if (Versions.TryGetValue(issuer.Id, out var previous) && previous != version)
            {
                cache.Remove($"issuer-secrets:{issuer.Id}:{previous}");
            }

            Versions[issuer.Id] = version;

            cache.Set(key, secrets, options.Value.SecretsCacheLifetime);
        }

        return ProviderIssuer.From(issuer, secrets);
    }
}

/// <summary>
/// The idempotency key of a document: the same on every send.
/// </summary>
public static class IdempotencyKeys
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// The id of the document written in base 36: 25 characters, inside the 30 the strictest provider accepts.
    /// </summary>
    /// <param name="documentId">The id of the document.</param>
    /// <returns>The key.</returns>
    public static string For(Guid documentId)
    {
        var value = new BigInteger(documentId.ToByteArray(), isUnsigned: true, isBigEndian: false);
        var digits = new char[25];

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            digits[i] = Alphabet[(int)(value % 36)];
            value /= 36;
        }

        return new string(digits);
    }
}
