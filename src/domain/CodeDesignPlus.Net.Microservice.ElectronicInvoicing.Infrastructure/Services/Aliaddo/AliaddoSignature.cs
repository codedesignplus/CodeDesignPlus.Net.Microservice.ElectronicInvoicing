using System.Security.Cryptography;
using System.Text;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// The signature of the calls of Aliaddo: an HMAC of the body with the shared secret, with the header, the algorithm
/// and the encoding of the configuration (the documentation does not publish them).
/// </summary>
public static class AliaddoSignature
{
    /// <summary>
    /// The HMAC of a configured algorithm.
    /// </summary>
    /// <param name="algorithm">The name of the algorithm.</param>
    /// <returns>A function that signs with a key, or null when the algorithm is not known.</returns>
    public static Func<byte[], byte[], byte[]>? AlgorithmOf(string? algorithm) => algorithm?.ToUpperInvariant() switch
    {
        "HMACSHA256" => HMACSHA256.HashData,
        "HMACSHA512" => HMACSHA512.HashData,
        "HMACSHA1" => HMACSHA1.HashData,
        _ => null
    };

    /// <summary>
    /// Whether a signature matches the body.
    /// </summary>
    /// <param name="body">The body, as it came.</param>
    /// <param name="signature">The signature of the header.</param>
    /// <param name="options">How the calls are signed.</param>
    /// <returns>Whether it matches, compared in constant time.</returns>
    public static bool Matches(string body, string? signature, AliaddoWebhooksOptions options)
    {
        var sign = AlgorithmOf(options.Algorithm);

        if (sign is null || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(options.Secret))
        {
            return false;
        }

        var received = signature.Trim();

        if (!string.IsNullOrEmpty(options.SignaturePrefix) && received.StartsWith(options.SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            received = received[options.SignaturePrefix.Length..];
        }

        var expected = sign(Encoding.UTF8.GetBytes(options.Secret), Encoding.UTF8.GetBytes(body ?? string.Empty));
        var written = string.Equals(options.Encoding, "base64", StringComparison.OrdinalIgnoreCase) ? Convert.ToBase64String(expected) : Convert.ToHexStringLower(expected);
        var comparable = string.Equals(options.Encoding, "base64", StringComparison.OrdinalIgnoreCase) ? received : received.ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(written), Encoding.UTF8.GetBytes(comparable));
    }
}
