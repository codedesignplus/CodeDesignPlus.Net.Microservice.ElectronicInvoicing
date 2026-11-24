using System.Numerics;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;

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
