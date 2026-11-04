using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// Writes values in a stable text form, so the same content always gives the same hash.
/// </summary>
/// <remarks>
/// Each value is written with the invariant culture and escaped, so a separator inside a text cannot make two different
/// contents read the same. Absent values are written as an empty field.
/// </remarks>
public static class Canonicalizer
{
    /// <summary>
    /// Joins the values in a stable text form.
    /// </summary>
    /// <param name="values">The values, in a fixed order.</param>
    /// <returns>The canonical text.</returns>
    public static string Join(params object?[] values) => string.Join('|', values.Select(Write));

    /// <summary>
    /// Joins a list of already canonical values.
    /// </summary>
    /// <param name="values">The canonical values.</param>
    /// <returns>The canonical text of the list.</returns>
    public static string List(IEnumerable<string>? values) => "[" + string.Join(';', (values ?? []).Select(Escape)) + "]";

    /// <summary>
    /// The SHA-256 of a canonical text, in lowercase hexadecimal.
    /// </summary>
    /// <param name="canonical">The canonical text.</param>
    /// <returns>The hash.</returns>
    public static string Hash(string canonical) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    private static string Write(object? value) => value switch
    {
        null => string.Empty,
        string text => Escape(text),
        LocalDate date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Escape(value.ToString() ?? string.Empty)
    };

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("|", "\\|").Replace(";", "\\;").Replace("[", "\\[").Replace("]", "\\]");
}
