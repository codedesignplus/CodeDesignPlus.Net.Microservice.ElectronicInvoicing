using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// A provider that answers in memory, for development and tests: nothing reaches a tax authority.
/// </summary>
/// <remarks>
/// It is deterministic: the id in the provider and the CUFE derive from the document, and the outcome comes from the
/// rules of <c>Simulated:Rules</c>, chosen by the identification of the buyer (accept, reject, leave the tax authority
/// not answering for some checks, or not answer at all). It has the capabilities of a provider that works with the
/// platform's account and numbers nothing, so the whole flow runs against it. What it received lives in the memory of
/// the process: a restart forgets it, and the documents sent before are not found again.
/// </remarks>
/// <param name="options">The rules of the simulation.</param>
public sealed class SimulatedProvider(IOptions<SimulatedOptions> options) : IIssuerProvisioningProvider
{
    /// <summary>
    /// The code of the adapter.
    /// </summary>
    public const string ProviderCode = "Simulated";

    /// <summary>
    /// Namespace of the ids the simulation gives. Changing it changes the ids of the simulated documents.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("e7b3a915-2d4c-4c86-8f01-6a9d3b2c5e48");

    private readonly ConcurrentDictionary<string, SimulatedDocument> received = new();

    /// <inheritdoc/>
    public string Code => ProviderCode;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities { get; } = new(
        IssuerProvisioningMode.Api,
        CredentialScope.MasterAccount,
        NumberingMode.ByPlatform,
        HabilitationByApi: true,
        DocumentTypes: new HashSet<ElectronicDocumentType>(Enum.GetValues<ElectronicDocumentType>()),
        SupportsPrepayments: true,
        ExternalBillingReference: true,
        StatusUpdatesMode.Poll,
        IdempotencyMode.LookupByNumber,
        ProvidesXml: true,
        DeliversToBuyer: false,
        RequiresProviderCatalog: false);

    /// <inheritdoc/>
    public Task<CredentialsValidation> ValidateCredentialsAsync(ProviderIssuer issuer, CancellationToken cancellationToken) => Task.FromResult(new CredentialsValidation(true, null));

    /// <inheritdoc/>
    public Task<ProviderSubmission> SubmitAsync(ProviderIssuer issuer, ProviderDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(document);

        var rule = RuleFor(document.Content.Buyer.IdentificationNumber);

        if (rule.Outcome == SimulatedOutcome.ProviderUnavailable)
        {
            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderUnavailable.GetCode(), Errors.ProviderUnavailable.GetMessage());
        }

        var id = DeterministicGuid.Create(IdNamespace, document.DocumentId.ToString()).ToString();
        var issueDate = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date;
        var simulated = received.AddOrUpdate(id, _ => new SimulatedDocument(id, document, rule, issueDate), (_, previous) => previous with { Document = document, Rule = rule, Checks = 0 });

        return Task.FromResult(new ProviderSubmission(id, StateOf(simulated)));
    }

    /// <inheritdoc/>
    public Task<ProviderDocumentState?> GetStateAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var found = Find(lookup);

        if (found is null)
        {
            return Task.FromResult<ProviderDocumentState?>(null);
        }

        found = received.AddOrUpdate(found.Id, found, (_, previous) => previous with { Checks = previous.Checks + 1 });

        return Task.FromResult<ProviderDocumentState?>(StateOf(found));
    }

    /// <inheritdoc/>
    public Task<ProviderFiles> GetFilesAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        var found = Find(lookup);

        if (found?.Document.Number is not { } number)
        {
            return Task.FromResult(ProviderFiles.None);
        }

        var name = number.ToString();
        var xml = Encoding.UTF8.GetBytes($"<SimulatedDocument number=\"{name}\" cufe=\"{CufeOf(found)}\" />");
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\n% simulated {name}\n%%EOF\n");

        return Task.FromResult(new ProviderFiles(new ProviderFile($"{name}.xml", xml), null, new ProviderFile($"{name}.pdf", pdf)));
    }

    /// <inheritdoc/>
    public Task<ProvisionedIssuer> ProvisionAsync(ProviderIssuer issuer, IssuerContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        return Task.FromResult(new ProvisionedIssuer(DeterministicGuid.Create(IdNamespace, issuer.Tenant.ToString()).ToString(), "simulated"));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProviderNumberingRange>> GetNumberingRangesAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderNumberingRange> ranges = [.. options.Value.Resolutions.Select(x => new ProviderNumberingRange(x.TechnicalKey, x.Prefix, x.ResolutionNumber, x.From, x.To, LocalDate.FromDateOnly(x.ValidFrom), LocalDate.FromDateOnly(x.ValidUntil)))];

        return Task.FromResult(ranges);
    }

    private SimulatedRule RuleFor(string identification) => options.Value.Rules.FirstOrDefault(x => string.Equals(x.BuyerIdentification, identification, StringComparison.Ordinal))
        ?? new SimulatedRule { Outcome = options.Value.DefaultOutcome };

    private SimulatedDocument? Find(ProviderDocumentLookup lookup)
    {
        if (!string.IsNullOrWhiteSpace(lookup.ProviderDocumentId) && received.TryGetValue(lookup.ProviderDocumentId, out var byId))
        {
            return byId;
        }

        return lookup.Number is null ? null : received.Values.FirstOrDefault(x => x.Document.Type == lookup.Type && x.Document.Number == lookup.Number);
    }

    private static ProviderDocumentState StateOf(SimulatedDocument document)
    {
        var rule = document.Rule;

        return rule.Outcome switch
        {
            SimulatedOutcome.Rejected => new ProviderDocumentState(ProviderDocumentStatus.Rejected, document.Id, null, document.Document.Number, document.IssueDate, [rule.Reason ?? "Simulated rejection."], RejectionOrigin.Dian, "Rejected"),
            SimulatedOutcome.DianUnavailable when document.Checks < rule.ChecksUntilResolved => new ProviderDocumentState(ProviderDocumentStatus.DianUnavailable, document.Id, null, document.Document.Number, document.IssueDate, [], null, "DianUnavailable"),
            SimulatedOutcome.Processing when document.Checks < rule.ChecksUntilResolved => new ProviderDocumentState(ProviderDocumentStatus.Processing, document.Id, null, document.Document.Number, document.IssueDate, [], null, "Processing"),
            _ => new ProviderDocumentState(ProviderDocumentStatus.Accepted, document.Id, CufeOf(document), document.Document.Number, document.IssueDate, [], null, "Accepted")
        };
    }

    private static string CufeOf(SimulatedDocument document) => Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes($"{document.Document.DocumentId}|{document.Document.Number}")));

    private sealed record SimulatedDocument(string Id, ProviderDocument Document, SimulatedRule Rule, LocalDate IssueDate, int Checks = 0);
}

/// <summary>
/// The rules of the simulated provider.
/// </summary>
public class SimulatedOptions
{
    /// <summary>
    /// Name of the section in the configuration.
    /// </summary>
    public static readonly string Section = "Simulated";

    /// <summary>
    /// What happens to a document whose buyer matches no rule.
    /// </summary>
    public SimulatedOutcome DefaultOutcome { get; set; } = SimulatedOutcome.Accepted;

    /// <summary>
    /// What happens to the documents of a buyer, by its identification.
    /// </summary>
    public List<SimulatedRule> Rules { get; set; } = [];

    /// <summary>
    /// The numbering resolutions the simulation has for every issuer.
    /// </summary>
    public List<SimulatedResolution> Resolutions { get; set; } = [];
}

/// <summary>
/// What happens to the documents of a buyer in the simulation.
/// </summary>
public class SimulatedRule
{
    /// <summary>
    /// The identification of the buyer the rule applies to.
    /// </summary>
    public string? BuyerIdentification { get; set; }

    /// <summary>
    /// What happens.
    /// </summary>
    public SimulatedOutcome Outcome { get; set; } = SimulatedOutcome.Accepted;

    /// <summary>
    /// The reason of a rejection.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// How many checks a document waits before it is accepted, when the outcome is to wait.
    /// </summary>
    public int ChecksUntilResolved { get; set; } = 1;
}

/// <summary>
/// A numbering resolution of the simulation.
/// </summary>
public class SimulatedResolution
{
    /// <summary>
    /// The technical key.
    /// </summary>
    public string? TechnicalKey { get; set; }

    /// <summary>
    /// The prefix.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// The number of the resolution.
    /// </summary>
    public string ResolutionNumber { get; set; } = string.Empty;

    /// <summary>
    /// The first number.
    /// </summary>
    public long From { get; set; }

    /// <summary>
    /// The last number.
    /// </summary>
    public long To { get; set; }

    /// <summary>
    /// The first day of the validity.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>
    /// The last day of the validity.
    /// </summary>
    public DateOnly ValidUntil { get; set; }
}

/// <summary>
/// What the simulated provider does with a document.
/// </summary>
public enum SimulatedOutcome
{
    /// <summary>
    /// It is validated at once.
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// The tax authority rejects it.
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// The tax authority does not answer for some checks, then it is validated.
    /// </summary>
    DianUnavailable = 3,

    /// <summary>
    /// It stays processing for some checks, then it is validated.
    /// </summary>
    Processing = 4,

    /// <summary>
    /// The provider does not answer the send.
    /// </summary>
    ProviderUnavailable = 5
}
