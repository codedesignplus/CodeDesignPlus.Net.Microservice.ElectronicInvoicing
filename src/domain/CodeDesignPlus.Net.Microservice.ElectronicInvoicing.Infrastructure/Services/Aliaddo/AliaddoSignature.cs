using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// The signature of the calls of Aliaddo. It travels in the body, not in a header:
/// <c>{"signature":{"timestamp","token","signature"},"eventData":{…}}</c>, where <c>signature</c> is the HMAC-SHA256,
/// in hexadecimal, of the timestamp followed by the token, with the signing key of the portal
/// (Configuración › Webhooks) as UTF-8 text. The timestamp is in seconds since the Unix epoch.
/// </summary>
/// <remarks>
/// The documentation does not publish it: it was found with the tester of the portal on 11-nov-2026 and confirmed with
/// the signing key on 19-nov-2026. The signature does not cover <c>eventData</c>, so a captured call could be sent again
/// with another content: a call is only a notice, an old one is discarded and the result is always asked to Nitro.
/// </remarks>
public static class AliaddoSignature
{
    /// <summary>
    /// Whether a call is signed with the key and is recent.
    /// </summary>
    /// <param name="body">The body, as it came.</param>
    /// <param name="secret">The signing key.</param>
    /// <param name="tolerance">How far from now the timestamp of the call may be.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>Whether it can be trusted; the signatures are compared in constant time.</returns>
    public static bool Matches(string? body, string? secret, TimeSpan tolerance, Instant now)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(body);

            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("signature", out var signed) || signed.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var timestamp = TextOf(signed, "timestamp");
            var token = TextOf(signed, "token");
            var signature = TextOf(signed, "signature");

            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signature) || !IsRecent(timestamp, tolerance, now))
            {
                return false;
            }

            var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + token)));

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsRecent(string timestamp, TimeSpan tolerance, Instant now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds > Instant.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        var age = (now - Instant.FromUnixTimeSeconds(seconds)).ToTimeSpan();

        return age <= tolerance && age >= -tolerance;
    }

    /// <summary>
    /// A property of an object as text, whether it came as a text or as a number.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="property">The name of the property.</param>
    /// <returns>The text, or null when it is not there.</returns>
    public static string? TextOf(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
