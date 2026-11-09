using System.Globalization;
using System.Text.Json.Nodes;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

/// <summary>
/// A document as Alanube answers it, whatever its type: the answer wraps it in a property named after the type
/// (<c>invoice</c>, <c>creditNote</c>, <c>equivalentDocument</c>…), next to its <c>files</c>.
/// </summary>
/// <param name="Id">The id in Alanube.</param>
/// <param name="Status">The status of the emission: <c>REGISTERED</c>, <c>WAITING_RESPONSE</c>, <c>FAILED</c> or <c>SENT</c>.</param>
/// <param name="LegalStatus">The answer of the tax authority: <c>ACCEPTED</c>, <c>ACCEPTED_WITH_OBSERVATIONS</c> or <c>REJECTED</c>.</param>
/// <param name="Cufe">The CUFE, or the CUDE of the notes and the documents of the point of sale.</param>
/// <param name="Number">The number.</param>
/// <param name="FullNumber">The prefix and the number together.</param>
/// <param name="IssueDate">The day of issue.</param>
/// <param name="GovernmentCode">The code of the answer of the tax authority.</param>
/// <param name="GovernmentMessage">The message of the answer of the tax authority.</param>
/// <param name="GovernmentMessages">The rules the tax authority reported: rejections or notifications.</param>
/// <param name="Errors">The errors of Alanube, as <c>code: message</c>.</param>
/// <param name="Files">The signed links of the files; they last an hour.</param>
public sealed record AlanubeDocument(string Id, string? Status, string? LegalStatus, string? Cufe, DocumentNumber? Number, string? FullNumber, LocalDate? IssueDate, string? GovernmentCode, string? GovernmentMessage, IReadOnlyList<string> GovernmentMessages, IReadOnlyList<string> Errors, AlanubeFiles Files)
{
    private const string Accepted = "ACCEPTED";
    private const string AcceptedWithObservations = "ACCEPTED_WITH_OBSERVATIONS";
    private const string Rejected = "REJECTED";
    private const string Failed = "FAILED";

    /// <summary>
    /// Whether the tax authority accepted it, with or without observations.
    /// </summary>
    public bool IsAccepted => LegalStatus is Accepted or AcceptedWithObservations;

    /// <summary>
    /// Whether the tax authority rejected it.
    /// </summary>
    public bool IsRejected => LegalStatus == Rejected;

    /// <summary>
    /// Reads the document of an answer of Alanube.
    /// </summary>
    /// <param name="root">The body of the answer.</param>
    /// <returns>The document, or null when the body carries none.</returns>
    public static AlanubeDocument? From(JsonNode? root)
    {
        if (root is not JsonObject body)
        {
            return null;
        }

        if (body["id"] is not null)
        {
            return Read(body, null);
        }

        var wrapped = body.Where(x => x.Key is not "files" and not "metadata" && x.Value is JsonObject inner && inner["id"] is not null).Select(x => (JsonObject)x.Value!).FirstOrDefault();

        return wrapped is null ? null : Read(wrapped, body["files"] as JsonObject);
    }

    /// <summary>
    /// Reads the documents of a list of Alanube (<c>{"metadata": …, "invoices": [ … ]}</c>).
    /// </summary>
    /// <param name="root">The body of the answer.</param>
    /// <returns>The documents.</returns>
    public static IReadOnlyList<AlanubeDocument> ListFrom(JsonNode? root)
    {
        if (root is not JsonObject body)
        {
            return [];
        }

        var list = body.Where(x => x.Key != "metadata" && x.Value is JsonArray).Select(x => (JsonArray)x.Value!).FirstOrDefault();

        return list is null ? [] : [.. list.OfType<JsonObject>().Where(x => x["id"] is not null).Select(x => Read(x, x["files"] as JsonObject))];
    }

    /// <summary>
    /// How the document stands, normalized.
    /// </summary>
    /// <param name="fallbackNumber">The number this service gave, when Alanube does not repeat it.</param>
    /// <returns>The state.</returns>
    /// <remarks>
    /// The legal status rules over the rest: a rejected document carries its CUFE too. <c>FAILED</c> without a legal
    /// status is a send Alanube could not finish: it is a rejection of the provider, which a correction sends again (on
    /// the same number, as Alanube allows). Anything else is still processing: <c>REGISTERED</c> and
    /// <c>WAITING_RESPONSE</c> are the tax authority not answering yet, and the status job asks again.
    /// </remarks>
    public ProviderDocumentState StateOf(DocumentNumber? fallbackNumber)
    {
        var raw = string.IsNullOrWhiteSpace(LegalStatus) ? Status : $"{Status}/{LegalStatus}";
        var number = Number ?? fallbackNumber;

        if (IsRejected)
        {
            return new ProviderDocumentState(ProviderDocumentStatus.Rejected, Id, null, number, IssueDate, GovernmentReasons(), RejectionOrigin.Dian, raw);
        }

        if (IsAccepted)
        {
            // The observations of an acceptance are kept as its reasons: the document is valid, and they say what to fix.
            return new ProviderDocumentState(ProviderDocumentStatus.Accepted, Id, Cufe, number, IssueDate, GovernmentMessages, null, raw);
        }

        if (Status == Failed)
        {
            var reasons = Errors.Concat(GovernmentMessages).ToList();

            return new ProviderDocumentState(ProviderDocumentStatus.Rejected, Id, null, number, IssueDate, reasons.Count > 0 ? reasons : [Failed], RejectionOrigin.Provider, raw);
        }

        return new ProviderDocumentState(ProviderDocumentStatus.Processing, Id, null, number, IssueDate, [], null, raw);
    }

    private List<string> GovernmentReasons()
    {
        var reasons = GovernmentMessages.ToList();

        if (reasons.Count == 0 && !string.IsNullOrWhiteSpace(GovernmentMessage))
        {
            reasons.Add(string.IsNullOrWhiteSpace(GovernmentCode) ? GovernmentMessage : $"{GovernmentCode}: {GovernmentMessage}");
        }

        return reasons;
    }

    private static AlanubeDocument Read(JsonObject document, JsonObject? files)
    {
        var government = document["governmentResponse"] as JsonObject;
        var prefix = Text(document["prefix"]);
        var consecutive = LongOf(document["number"]);

        var errors = (document["errors"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(x => string.IsNullOrWhiteSpace(Text(x["code"])) ? Text(x["message"]) : $"{Text(x["code"])}: {Text(x["message"])}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        var messages = (government?["errorMessages"] as JsonArray ?? []).Select(Text).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

        return new AlanubeDocument(
            Text(document["id"])!,
            Text(document["status"]),
            Text(document["legalStatus"]),
            Text(document["cufe"]) ?? Text(document["cude"]),
            prefix is not null && consecutive is { } value ? new DocumentNumber(prefix, value) : null,
            Text(document["fullNumber"]),
            DateOf(Text(document["date"])),
            Text(government?["code"]),
            Text(government?["message"]),
            messages,
            errors,
            new AlanubeFiles(UrlOf(files?["xml"]), UrlOf(files?["attachedDocument"]), UrlOf(files?["zip"])));
    }

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value => value.ToJsonString(),
        _ => null
    };

    // The number is a number in the invoice and the credit note, and a text in the debit note and the point of sale.
    private static long? LongOf(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<double>(out var real))
        {
            return (long)real;
        }

        return value.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static Uri? UrlOf(JsonNode? node) => Text(node) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;

    private static LocalDate? DateOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
        {
            return null;
        }

        // The day as Alanube wrote it, in the time zone of the issue (2026-11-09T17:48:27-05:00 is the 9th).
        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? LocalDate.FromDateOnly(date) : null;
    }
}

/// <summary>
/// The signed links of the files of a document.
/// </summary>
/// <param name="Xml">The XML sent to the tax authority.</param>
/// <param name="AttachedDocument">The attached document with the answer of the tax authority.</param>
/// <param name="Zip">The package with the attached document and the PDF.</param>
public sealed record AlanubeFiles(Uri? Xml, Uri? AttachedDocument, Uri? Zip);
