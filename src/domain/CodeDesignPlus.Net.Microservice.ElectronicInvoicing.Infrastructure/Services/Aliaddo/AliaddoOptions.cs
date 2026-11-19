using System.ComponentModel.DataAnnotations;
using CodeDesignPlus.Net.Resilience.Abstractions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

/// <summary>
/// How to reach Aliaddo Nitro and how each type of document is sent there.
/// </summary>
/// <remarks>
/// Built from the published documentation (<c>docs/aliaddo/</c> of the monorepo) and from what the tests against
/// Nitro showed (the signature of the webhooks and where the files are downloaded, which it does not publish).
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
        foreach (var type in AliaddoProvider.SupportedTypes)
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

        if (Webhooks.Enabled && string.IsNullOrWhiteSpace(Webhooks.Secret))
        {
            yield return new ValidationResult("Aliaddo:Webhooks needs Secret when it is enabled.", [nameof(Webhooks)]);
        }

        if (Webhooks.Tolerance <= TimeSpan.Zero)
        {
            yield return new ValidationResult("Aliaddo:Webhooks:Tolerance must be greater than zero.", [nameof(Webhooks)]);
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
/// The calls of Aliaddo with the result of the documents, signed as <see cref="AliaddoSignature"/> says. Off by
/// default: while it is off the status job asks for every result.
/// </summary>
public class AliaddoWebhooksOptions
{
    /// <summary>
    /// Whether the calls are received; while it is off the status job asks for every result.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The signing key of the portal (Configuración › Webhooks). It comes from the Vault.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// How far from now the timestamp of a call may be. The signature does not cover the content of the call, so an
    /// older one is discarded: it could be a captured call sent again.
    /// </summary>
    public TimeSpan Tolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Never prints the secret.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(AliaddoWebhooksOptions)} {{ enabled: {Enabled}, secret: {!string.IsNullOrWhiteSpace(Secret)} }}";
}
