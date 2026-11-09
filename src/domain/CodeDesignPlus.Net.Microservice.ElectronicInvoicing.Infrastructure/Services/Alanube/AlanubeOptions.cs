using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Resilience.Abstractions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

/// <summary>
/// How to reach Alanube (the API of Alegra as electronic provider) and how its answers are read.
/// </summary>
/// <remarks>
/// Built from the documentation copied in <c>docs/alanube/</c> of the monorepo and checked against the sandbox. The base
/// of the API chooses the environment: the sandbox goes to the enablement environment of the tax authority and the
/// production base to its production, and each has its own companies.
/// </remarks>
public class AlanubeOptions : IValidatableObject
{
    /// <summary>
    /// Name of the section in the configuration.
    /// </summary>
    public static readonly string Section = "Alanube";

    /// <summary>
    /// The base of the API, with its version (<c>…/e-provider/col/v1</c>).
    /// </summary>
    [Required]
    public Uri BaseUrl { get; set; } = null!;

    /// <summary>
    /// The account of the platform: the main company, which creates and operates every associated company.
    /// </summary>
    public AlanubeMasterOptions Master { get; set; } = new();

    /// <summary>
    /// How each type of document is sent, by the name of the type (<c>Invoice</c>, <c>CreditNote</c>, <c>DebitNote</c>).
    /// </summary>
    public Dictionary<string, AlanubeDocumentOptions> Documents { get; set; } = [];

    /// <summary>
    /// The code of the payment form that is a credit in the tax authority's list: only then the due date is sent.
    /// </summary>
    [Required]
    public string CreditPaymentForm { get; set; } = null!;

    /// <summary>
    /// The tax scheme of the issuer (<c>taxCode.id</c> of the company) by the code of its tax regime; a regime that is
    /// not here sends no tax scheme.
    /// </summary>
    public Dictionary<string, string> TaxSchemeByRegime { get; set; } = [];

    /// <summary>
    /// Whether the companies are created with the certificate of Alegra, so no issuer buys one.
    /// </summary>
    public bool UseProviderCertificate { get; set; } = true;

    /// <summary>
    /// Whether Alanube mails the document to the buyer when the tax authority accepts it.
    /// </summary>
    public bool NotifyBuyerByEmail { get; set; }

    /// <summary>
    /// The codes of the errors of Alanube that are read as something other than a refusal of the document.
    /// </summary>
    public AlanubeErrorCodesOptions ErrorCodes { get; set; } = new();

    /// <summary>
    /// The calls of Alanube with the result of the documents.
    /// </summary>
    public AlanubeWebhooksOptions Webhooks { get; set; } = new();

    /// <summary>
    /// Timeouts, retries and circuit breaker of the reads. The sends never retry by themselves: a send that timed out
    /// is sent again by the next round with the same idempotency key.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// How long a send waits for Alanube before it is taken as not answered. The emission is synchronous: it includes
    /// the answer of the tax authority.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How a type of document is sent.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Its options; empty when the type needs none.</returns>
    public AlanubeDocumentOptions For(ElectronicDocumentType type) => Documents.TryGetValue(type.ToString(), out var options) ? options : new AlanubeDocumentOptions();

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(For(ElectronicDocumentType.Invoice).DocumentType))
        {
            yield return new ValidationResult("Alanube:Documents:Invoice needs its DocumentType.", [nameof(Documents)]);
        }

        if (string.IsNullOrWhiteSpace(For(ElectronicDocumentType.CreditNote).DocumentType))
        {
            yield return new ValidationResult("Alanube:Documents:CreditNote needs its DocumentType.", [nameof(Documents)]);
        }

        foreach (var note in new[] { ElectronicDocumentType.CreditNote, ElectronicDocumentType.DebitNote })
        {
            if (string.IsNullOrWhiteSpace(For(note).CorrectedDocumentType))
            {
                yield return new ValidationResult($"Alanube:Documents:{note} needs its CorrectedDocumentType.", [nameof(Documents)]);
            }
        }

        if (SendTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult("Alanube:SendTimeout must be greater than zero.", [nameof(SendTimeout)]);
        }

        if (Webhooks.Enabled && (Webhooks.Url is null || string.IsNullOrWhiteSpace(Webhooks.Header) || string.IsNullOrWhiteSpace(Webhooks.Secret)))
        {
            yield return new ValidationResult("Alanube:Webhooks needs Url, Header and Secret when it is enabled.", [nameof(Webhooks)]);
        }
    }
}

/// <summary>
/// The account of the platform in Alanube. Its token comes from the Vault or from the environment, never from a file
/// in the repository.
/// </summary>
public class AlanubeMasterOptions
{
    /// <summary>
    /// The token of the main company.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The configuration key (or environment variable) that holds the token when <see cref="Token"/> is empty, for
    /// example <c>ALANUBE_CO_TOKEN</c> on a developer's machine.
    /// </summary>
    public string? TokenKey { get; set; }

    /// <summary>
    /// Never prints the token.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(AlanubeMasterOptions)} {{ configured: {!string.IsNullOrWhiteSpace(Token)}, key: {TokenKey} }}";
}

/// <summary>
/// How a type of document is sent to Alanube.
/// </summary>
public class AlanubeDocumentOptions
{
    /// <summary>
    /// The code of the type of document in the tax authority's list (<c>documentType</c>): <c>01</c> for the standard
    /// invoice, <c>91</c> for the standard credit note.
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>
    /// The code of the type of the document a note corrects (<c>associatedDocuments[].documentType</c>).
    /// </summary>
    public string? CorrectedDocumentType { get; set; }
}

/// <summary>
/// The codes of the errors of Alanube (<c>docs/alanube/docs/errores.md</c>) that are not a refusal of the document.
/// </summary>
/// <remarks>
/// Configuration and not code: the list is Alanube's and grows with its API. A code that is in neither list is a
/// refusal of the content, which is not retried.
/// </remarks>
public class AlanubeErrorCodesOptions
{
    /// <summary>
    /// The issuer cannot issue: it is not enabled for the type of document, it does not exist in Alanube or its data
    /// there is incomplete. The document waits and the issuer is suspended when it keeps happening.
    /// </summary>
    public List<string> IssuerNotReady { get; set; } = [];

    /// <summary>
    /// A document with the number was already received (accepted or in process): it is looked up by its number and
    /// adopted instead of being taken as a refusal.
    /// </summary>
    public List<string> AlreadyReceived { get; set; } = [];
}

/// <summary>
/// The calls of Alanube with the result of the documents. They carry no signature: they are trusted by a header and a
/// secret this service gives Alanube when it creates each company.
/// </summary>
public class AlanubeWebhooksOptions
{
    /// <summary>
    /// Whether the calls are received; while it is off the status job asks for every result.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The public address of the endpoint of the calls (<c>…/api/ProviderEvents/Alanube</c>).
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// The header that carries the secret.
    /// </summary>
    public string? Header { get; set; }

    /// <summary>
    /// The secret. It comes from the Vault.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Never prints the secret.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(AlanubeWebhooksOptions)} {{ enabled: {Enabled}, secret: {!string.IsNullOrWhiteSpace(Secret)} }}";
}
