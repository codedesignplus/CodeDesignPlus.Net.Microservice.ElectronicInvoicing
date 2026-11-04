using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Resilience.Abstractions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// How to reach Aliaddo Nitro and how each type of document is sent there.
/// </summary>
/// <remarks>
/// Built only from the published documentation (<c>docs/aliaddo/</c> of the monorepo). What it does not publish —the
/// signature of the webhooks, where the relative URLs of the files point— is configuration with no default, and stays
/// off until it is validated in <c>mode = Test</c>.
/// </remarks>
public class AliaddoOptions : IValidatableObject
{
    /// <summary>
    /// Name of the section in the configuration.
    /// </summary>
    public static readonly string Section = "Aliaddo";

    /// <summary>
    /// The base of the API (one for every environment: the environment goes in the <c>mode</c> of each document).
    /// </summary>
    [Required]
    public Uri BaseUrl { get; set; } = null!;

    /// <summary>
    /// The account of the platform, the parent organization that operates every issuer.
    /// </summary>
    public AliaddoMasterOptions Master { get; set; } = new();

    /// <summary>
    /// How each type of document is sent: its code and type of operation in the tax authority's lists and the format of
    /// its PDF, by the name of the type (<c>Invoice</c>, <c>CreditNote</c>, <c>DebitNote</c>, <c>PosTicket</c>).
    /// </summary>
    public Dictionary<string, AliaddoDocumentOptions> Documents { get; set; } = [];

    /// <summary>
    /// The name printed on the discount of a line.
    /// </summary>
    [Required]
    public string LineDiscountName { get; set; } = null!;

    /// <summary>
    /// Where the relative URLs of the files of a document point (the documentation shows <c>UrlPdf</c> as a path
    /// without host). Null leaves the files in the provider until it is known.
    /// </summary>
    public Uri? FilesBaseUrl { get; set; }

    /// <summary>
    /// The calls of Aliaddo with the result of the documents.
    /// </summary>
    public AliaddoWebhooksOptions Webhooks { get; set; } = new();

    /// <summary>
    /// Timeouts, retries and circuit breaker of the reads. The sends never retry by themselves: a send whose answer was
    /// lost is looked up by its number before sending it again.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// How long a send waits for Aliaddo before it is taken as not answered.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How a type of document is sent.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Its options.</returns>
    public AliaddoDocumentOptions For(ElectronicDocumentType type) => Documents.TryGetValue(type.ToString(), out var options)
        ? options
        : throw new InvalidOperationException($"Aliaddo:Documents has no entry for {type}.");

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var type in Enum.GetValues<ElectronicDocumentType>())
        {
            if (!Documents.TryGetValue(type.ToString(), out var document) || string.IsNullOrWhiteSpace(document.Code) || string.IsNullOrWhiteSpace(document.OperationType) || string.IsNullOrWhiteSpace(document.Format))
            {
                yield return new ValidationResult($"Aliaddo:Documents:{type} needs Code, OperationType and Format.", [nameof(Documents)]);
            }
        }

        if (SendTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult("Aliaddo:SendTimeout must be greater than zero.", [nameof(SendTimeout)]);
        }

        if (Webhooks.Enabled && (string.IsNullOrWhiteSpace(Webhooks.SignatureHeader) || string.IsNullOrWhiteSpace(Webhooks.Secret) || string.IsNullOrWhiteSpace(Webhooks.DocumentIdProperty) || AliaddoSignature.AlgorithmOf(Webhooks.Algorithm) is null))
        {
            yield return new ValidationResult("Aliaddo:Webhooks needs SignatureHeader, Secret, DocumentIdProperty and a known Algorithm when it is enabled.", [nameof(Webhooks)]);
        }
    }
}

/// <summary>
/// The account of the platform in Aliaddo. Its token comes from the Vault, never from a file in the repository.
/// </summary>
public class AliaddoMasterOptions
{
    /// <summary>
    /// The token of the parent organization.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Never prints the token.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(AliaddoMasterOptions)} {{ configured: {!string.IsNullOrWhiteSpace(Token)} }}";
}

/// <summary>
/// How a type of document is sent to Aliaddo.
/// </summary>
public class AliaddoDocumentOptions
{
    /// <summary>
    /// The code of the type of document in the tax authority's list (<c>code</c>).
    /// </summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// The type of operation in the tax authority's list (<c>typeOperation</c>).
    /// </summary>
    public string OperationType { get; set; } = null!;

    /// <summary>
    /// The format of the PDF (<c>format</c>).
    /// </summary>
    public string Format { get; set; } = null!;
}

/// <summary>
/// The calls of Aliaddo with the result of the documents. Off by default: the documentation does not publish how they
/// are signed, so the header, the algorithm and the field that names the document are configuration.
/// </summary>
public class AliaddoWebhooksOptions
{
    /// <summary>
    /// Whether the calls are received; while it is off the status job asks for every result.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The header that carries the signature.
    /// </summary>
    public string? SignatureHeader { get; set; }

    /// <summary>
    /// The algorithm of the signature: <c>HMACSHA256</c>, <c>HMACSHA512</c> or <c>HMACSHA1</c>.
    /// </summary>
    public string Algorithm { get; set; } = "HMACSHA256";

    /// <summary>
    /// How the signature is written: <c>hex</c> or <c>base64</c>.
    /// </summary>
    public string Encoding { get; set; } = "hex";

    /// <summary>
    /// A text the signature carries before its value (for example <c>sha256=</c>), if any.
    /// </summary>
    public string? SignaturePrefix { get; set; }

    /// <summary>
    /// The secret of the signature. It comes from the Vault.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// The property of the body that carries the id of the document in Aliaddo.
    /// </summary>
    public string? DocumentIdProperty { get; set; }

    /// <summary>
    /// The property of the body that carries the status, if any.
    /// </summary>
    public string? StatusProperty { get; set; }

    /// <summary>
    /// Never prints the secret.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(AliaddoWebhooksOptions)} {{ enabled: {Enabled}, secret: {!string.IsNullOrWhiteSpace(Secret)} }}";
}
