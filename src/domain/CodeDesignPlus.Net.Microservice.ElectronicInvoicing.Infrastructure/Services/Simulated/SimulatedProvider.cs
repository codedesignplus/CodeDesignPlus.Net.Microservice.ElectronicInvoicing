using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Simulated;

/// <summary>
/// A provider that answers by itself, for development and tests: nothing reaches a tax authority.
/// </summary>
/// <remarks>
/// It is deterministic: the id in the provider and the CUFE derive from the document, and the outcome comes from the
/// rules of <c>Simulated:Rules</c>, chosen by the identification of the buyer (accept, reject, leave the tax authority
/// not answering for some checks, or not answer at all). It has the capabilities of a provider that works with the
/// platform's account and numbers nothing, so the whole flow runs against it.
/// <para>
/// What it received lives in the database (<see cref="ISimulatedProviderDocumentRepository"/>) and never in the memory
/// of the process: the service runs several replicas and restarts, and the one that answers a check is rarely the one
/// that received the document. A document it never received is still not found, and neither is one whose
/// <c>Simulated:Retention</c> passed: the database deletes it.
/// </para>
/// <para>
/// Its graphic representation is a real PDF (<see cref="SimulatedGraphicRepresentation"/>) with what the document says and
/// the mark that it is a test without fiscal validity, in the language of <c>Simulated:Language</c>.
/// </para>
/// <para>
/// A real provider sends the validated document to the buyer by email. The simulation has no mail, so it writes that
/// email (<see cref="IBuyerNoticeProvider"/>) and the service sends it through ms-emails with the files attached, saying in
/// its subject and in its body that the document is a test; <c>Simulated:NotifyBuyerByEmail</c> turns it off.
/// </para>
/// </remarks>
/// <param name="options">The rules of the simulation.</param>
/// <param name="documents">Where it keeps the documents it received.</param>
/// <param name="clock">The clock.</param>
public sealed class SimulatedProvider(IOptions<SimulatedOptions> options, ISimulatedProviderDocumentRepository documents, IClock clock) : IIssuerProvisioningProvider, IBuyerNoticeProvider
{
    /// <summary>
    /// The code of the adapter.
    /// </summary>
    public const string ProviderCode = "Simulated";

    /// <summary>
    /// Namespace of the ids the simulation gives. Changing it changes the ids of the simulated documents.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("e7b3a915-2d4c-4c86-8f01-6a9d3b2c5e48");

    /// <summary>
    /// The reason of the rejection of a point of sale ticket that reaches the simulation without its register, named after
    /// the rule of the tax authority it would break. It is data of the provider, as its other reasons, and is not translated.
    /// </summary>
    public const string MissingPointOfSaleReason = "DEPD24: the point of sale ticket does not say the data of its cash register (InformacionCajaVenta).";

    /// <inheritdoc/>
    public string Code => ProviderCode;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities { get; } = new(
        NumberingMode.ByPlatform,
        HabilitationByApi: true,
        DocumentTypes: new HashSet<ElectronicDocumentType>(Enum.GetValues<ElectronicDocumentType>()),
        SupportsPrepayments: true,
        ExternalBillingReference: true,
        StatusUpdatesMode.Poll,
        IdempotencyMode.LookupByNumber,
        ProvidesXml: true,
        DeliversToBuyer: options.Value.NotifyBuyerByEmail,
        RequiresProviderCatalog: false)
    {
        // The simulated document carries the issue date it is sent with.
        IssuerSetsIssueDate = true
    };

    /// <inheritdoc/>
    public async Task<ProviderSubmission> SubmitAsync(ProviderIssuer issuer, ProviderDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(document);

        var rule = RuleOf(document);

        if (rule.Outcome == SimulatedOutcome.ProviderUnavailable)
        {
            throw new CodeDesignPlusException(Layer.Infrastructure, Errors.ProviderUnavailable.GetCode(), Errors.ProviderUnavailable.GetMessage());
        }

        var now = clock.GetCurrentInstant();

        var simulated = await documents.ReceiveAsync(
            new SimulatedProviderDocument
            {
                Id = DeterministicGuid.Create(IdNamespace, document.DocumentId.ToString()),
                Tenant = issuer.Tenant,
                IssuerId = issuer.IssuerId,
                DocumentId = document.DocumentId,
                Type = document.Type,
                Number = document.Number,
                NumberingRange = SimulatedNumberingRange.Of(document.NumberingRange),
                Content = document.Content,
                IssueDate = now.InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date,
                Outcome = rule.Outcome,
                Reason = rule.Reason,
                ChecksUntilResolved = rule.ChecksUntilResolved,
                FirstReceivedAt = now,
                ReceivedAt = now,
                ExpiresAt = options.Value.Retention > TimeSpan.Zero ? now + Duration.FromTimeSpan(options.Value.Retention) : null
            },
            cancellationToken);

        return new ProviderSubmission(IdOf(simulated), StateOf(simulated));
    }

    /// <inheritdoc/>
    public async Task<ProviderDocumentState?> GetStateAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(lookup);

        // Asking is what moves a document that waits: the check is counted where every replica sees it.
        var found = await documents.CheckAsync(KeyOf(issuer, lookup), cancellationToken);

        return found is null ? null : StateOf(found);
    }

    /// <inheritdoc/>
    public async Task<ProviderFiles> GetFilesAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(lookup);

        var found = await documents.FindAsync(KeyOf(issuer, lookup), cancellationToken);

        if (found?.Number is not { } number)
        {
            return ProviderFiles.None;
        }

        var name = number.ToString();
        var xml = Encoding.UTF8.GetBytes(XmlOf(found, name));
        var pdf = SimulatedGraphicRepresentation.Render(Texts(), issuer, found.ToProviderDocument(), number, CufeOf(found), found.IssueDate);

        return new ProviderFiles(new ProviderFile($"{name}.xml", xml), null, new ProviderFile($"{name}.pdf", pdf));
    }

    /// <inheritdoc/>
    public async Task<BuyerNotice?> GetBuyerNoticeAsync(ProviderIssuer issuer, ProviderDocumentLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!options.Value.NotifyBuyerByEmail)
        {
            return null;
        }

        var found = await documents.FindAsync(KeyOf(issuer, lookup), cancellationToken);

        if (found?.Number is not { } number || StateOf(found).Status != ProviderDocumentStatus.Accepted || string.IsNullOrWhiteSpace(found.Content.Buyer.Email))
        {
            return null;
        }

        return SimulatedBuyerNotice.Write(Texts(), issuer, found.ToProviderDocument(), number);
    }

    /// <inheritdoc/>
    public Task<ProvisionedIssuer> ProvisionAsync(ProviderIssuer issuer, IssuerContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        return Task.FromResult(new ProvisionedIssuer(DeterministicGuid.Create(IdNamespace, issuer.Tenant.ToString()).ToString(), "simulated"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It keeps nothing of the issuer: every document carries its fiscal data, so there is nothing to replace.
    /// </remarks>
    public Task UpdateIssuerAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProviderNumberingRange>> GetNumberingRangesAsync(ProviderIssuer issuer, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderNumberingRange> ranges = [.. options.Value.Resolutions.Select(x => new ProviderNumberingRange(x.TechnicalKey, x.Prefix, x.ResolutionNumber, x.From, x.To, LocalDate.FromDateOnly(x.ValidFrom), LocalDate.FromDateOnly(x.ValidUntil)))];

        return Task.FromResult(ranges);
    }

    /// <summary>
    /// What happens to a document: a point of sale ticket without the data of its register is rejected, as the tax
    /// authority does, whatever the rules say; the rest follow the rule of their buyer.
    /// </summary>
    private SimulatedRule RuleOf(ProviderDocument document)
    {
        if (document.Type == ElectronicDocumentType.PosTicket && document.Content.PointOfSale is not { IsComplete: true })
        {
            return new SimulatedRule { Outcome = SimulatedOutcome.Rejected, Reason = MissingPointOfSaleReason };
        }

        return RuleFor(document.Content.Buyer.IdentificationNumber);
    }

    /// <summary>
    /// The simulated XML: the number and the CUFE and, in a document sold at a register, the data of the register as they
    /// were received, so a test can read what reached the provider.
    /// </summary>
    private static string XmlOf(SimulatedProviderDocument document, string name)
    {
        var root = new XElement("SimulatedDocument", new XAttribute("number", name), new XAttribute("cufe", CufeOf(document)));

        if (document.Content.PointOfSale is { } pointOfSale)
        {
            root.Add(new XElement(
                "PointOfSale",
                new XAttribute("registerCode", pointOfSale.RegisterCode),
                new XAttribute("registerType", pointOfSale.RegisterType),
                new XAttribute("locationName", pointOfSale.LocationName),
                new XAttribute("locationAddress", pointOfSale.LocationAddress),
                new XAttribute("cashierName", pointOfSale.CashierName),
                new XAttribute("saleCode", pointOfSale.SaleCode),
                new XAttribute("subtotal", document.Content.Totals.Subtotal)));
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The texts of the language of the deployment. The startup refuses a language without texts, so they are always there.
    /// </summary>
    private SimulatedTexts Texts() => SimulatedTexts.Find(options.Value.Language) ?? throw new InvalidOperationException($"The simulated provider has no texts in the language '{options.Value.Language}'.");

    private SimulatedRule RuleFor(string identification) => options.Value.Rules.FirstOrDefault(x => string.Equals(x.BuyerIdentification, identification, StringComparison.Ordinal))
        ?? new SimulatedRule { Outcome = options.Value.DefaultOutcome };

    /// <summary>
    /// How to find what a lookup names: by the id the simulation gave and, failing that, by the number, always among the
    /// documents of the issuer that asks. An id that is not one of the simulation's finds nothing by itself.
    /// </summary>
    private static SimulatedDocumentKey KeyOf(ProviderIssuer issuer, ProviderDocumentLookup lookup) => new(
        issuer.Tenant,
        issuer.IssuerId,
        Guid.TryParse(lookup.ProviderDocumentId, out var id) ? id : null,
        lookup.Type,
        lookup.Number);

    /// <summary>
    /// The id of a document as the simulation hands it over.
    /// </summary>
    private static string IdOf(SimulatedProviderDocument document) => document.Id.ToString();

    private static ProviderDocumentState StateOf(SimulatedProviderDocument document)
    {
        var id = IdOf(document);

        return document.Outcome switch
        {
            SimulatedOutcome.Rejected => new ProviderDocumentState(ProviderDocumentStatus.Rejected, id, null, document.Number, document.IssueDate, [document.Reason ?? "Simulated rejection."], RejectionOrigin.Dian, "Rejected"),
            SimulatedOutcome.DianUnavailable when document.Checks < document.ChecksUntilResolved => new ProviderDocumentState(ProviderDocumentStatus.DianUnavailable, id, null, document.Number, document.IssueDate, [], null, "DianUnavailable"),
            SimulatedOutcome.Processing when document.Checks < document.ChecksUntilResolved => new ProviderDocumentState(ProviderDocumentStatus.Processing, id, null, document.Number, document.IssueDate, [], null, "Processing"),
            _ => new ProviderDocumentState(ProviderDocumentStatus.Accepted, id, CufeOf(document), document.Number, document.IssueDate, [], null, "Accepted")
        };
    }

    private static string CufeOf(SimulatedProviderDocument document) => Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes($"{document.DocumentId}|{document.Number}")));
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

    /// <summary>
    /// The language of what the simulation writes for a person to read (the graphic representation and the email to the
    /// buyer): one of the embedded <c>Resources/simulated.&lt;language&gt;.json</c>.
    /// </summary>
    public string Language { get; set; } = "es";

    /// <summary>
    /// Whether a validated document is sent to the email of its buyer, as a real provider does, saying it is a test. A
    /// buyer without email gets nothing.
    /// </summary>
    public bool NotifyBuyerByEmail { get; set; } = true;

    /// <summary>
    /// How long the simulation keeps a document after its last send; the database deletes it afterwards, and from then
    /// on the simulation answers that it does not know it. What the flow needs of the simulation (the state, the files
    /// and the email) happens within minutes of the send. Zero (<c>00:00:00</c>) keeps the documents forever; an empty
    /// value does not, it leaves this default.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);
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
