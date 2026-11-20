using System.Net;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// The email with which the simulated provider delivers a validated document to its buyer, as a real provider would,
/// saying in its subject and in its body that the document is a test without fiscal validity.
/// </summary>
/// <remarks>
/// The subject is plain text. The title and the content are HTML that the template of ms-emails puts in as they come, so
/// everything that comes from the document (names, numbers) is encoded here.
/// </remarks>
internal static class SimulatedBuyerNotice
{
    /// <summary>
    /// Writes the email of a document.
    /// </summary>
    /// <param name="texts">The texts of the language.</param>
    /// <param name="issuer">The issuer.</param>
    /// <param name="document">The document as it was received; its buyer has an email.</param>
    /// <param name="number">Its number.</param>
    /// <returns>The email.</returns>
    public static BuyerNotice Write(SimulatedTexts texts, ProviderIssuer issuer, ProviderDocument document, DocumentNumber number)
    {
        var totals = document.Content.Totals;
        var buyer = document.Content.Buyer;

        var values = new Dictionary<string, string>
        {
            ["{type}"] = texts.NameOf(document.Type),
            ["{number}"] = number.ToString(),
            ["{issuer}"] = issuer.LegalName,
            ["{buyer}"] = buyer.Name,
            ["{total}"] = $"{totals.Currency} {SimulatedGraphicRepresentation.Money(totals.PayableAmount, totals, texts.NumberFormat())}"
        };

        var encoded = values.ToDictionary(x => x.Key, x => WebUtility.HtmlEncode(x.Value));

        var content = string.Concat(
            $"<p>{Fill(WebUtility.HtmlEncode(texts.EmailGreeting), encoded)}</p>",
            $"<p>{Fill(WebUtility.HtmlEncode(texts.EmailIntro), encoded)}</p>",
            $"<p style=\"padding: 12px 16px; background: #fef2f2; border: 1px solid #b91c1c; border-radius: 8px; color: #991b1b; font-weight: 600;\">{WebUtility.HtmlEncode(texts.EmailWarning)}</p>",
            $"<p>{WebUtility.HtmlEncode(texts.EmailAttachments)}</p>");

        return new BuyerNotice(buyer.Email!.Trim(), Fill(texts.EmailSubject, values), Fill(WebUtility.HtmlEncode(texts.EmailTitle), encoded), content);
    }

    private static string Fill(string text, Dictionary<string, string> values) => values.Aggregate(text, (filled, value) => filled.Replace(value.Key, value.Value, StringComparison.Ordinal));
}
