namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// A prepayment applied to an invoice.
/// </summary>
/// <param name="Id">The caller's id of the prepayment, as the tax authority asks for it.</param>
/// <param name="Amount">The amount applied, in minor units.</param>
/// <param name="ReceivedOn">When the prepayment was received, as a calendar date.</param>
public sealed record Prepayment(string Id, long Amount, LocalDate ReceivedOn)
{
    /// <summary>
    /// The prepayment in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(Id, Amount, ReceivedOn);
}

/// <summary>
/// How the document is paid, with the codes of the tax authority's lists.
/// </summary>
/// <param name="FormCode">Code of the form of payment (cash or credit).</param>
/// <param name="MethodCode">Code of the payment method (for example <c>10</c>, cash).</param>
public sealed record PaymentMeans(string FormCode, string MethodCode)
{
    /// <summary>
    /// The payment means in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(FormCode, MethodCode);
}

/// <summary>
/// The document a note corrects: one of this service, or one issued elsewhere identified by its number and CUFE.
/// </summary>
/// <param name="DocumentId">The id of the corrected document in this service, when it was issued here.</param>
/// <param name="Prefix">Prefix of the corrected document.</param>
/// <param name="Consecutive">Consecutive of the corrected document.</param>
/// <param name="Cufe">CUFE of the corrected document.</param>
/// <param name="IssueDate">Issue date of the corrected document.</param>
public sealed record BillingReference(Guid? DocumentId, string Prefix, long Consecutive, string Cufe, LocalDate IssueDate)
{
    /// <summary>
    /// The reference in a stable text form, for the content hash.
    /// </summary>
    /// <returns>The canonical text.</returns>
    public string Canonical() => Canonicalizer.Join(DocumentId, Prefix, Consecutive, Cufe, IssueDate);
}

/// <summary>
/// The number of an electronic document: the prefix of its range and its consecutive.
/// </summary>
/// <param name="Prefix">The prefix authorized in the resolution.</param>
/// <param name="Consecutive">The consecutive inside the range.</param>
public sealed record DocumentNumber(string Prefix, long Consecutive)
{
    /// <summary>
    /// The number as it is printed: prefix and consecutive together.
    /// </summary>
    /// <returns>The full number.</returns>
    public override string ToString() => string.Concat(Prefix, Consecutive.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Why a document was rejected, as the provider or the tax authority said it. The texts are data and are not
/// translated.
/// </summary>
/// <param name="Origin">Who rejected it.</param>
/// <param name="Reasons">The reasons, as they came.</param>
/// <param name="At">When it was rejected.</param>
public sealed record DocumentRejection(RejectionOrigin Origin, List<string> Reasons, Instant At);

/// <summary>
/// The authorization of the issuer for this service to issue on its behalf.
/// </summary>
/// <param name="Version">Version of the text that was accepted.</param>
/// <param name="AcceptedAt">When it was accepted.</param>
/// <param name="AcceptedBy">Who accepted it.</param>
/// <param name="File">The signed document in ms-filestorage, when there is one.</param>
public sealed record IssuerMandate(string Version, Instant AcceptedAt, Guid AcceptedBy, StoredFile? File);

/// <summary>
/// The credentials of an issuer that brings its own, encrypted by the Vault's Transit engine. Only ciphertext and the
/// last four characters of each value, so whoever looks can tell which credentials are connected.
/// </summary>
/// <param name="Ciphertexts">The ciphertext of each secret, by the name the adapter gives it.</param>
/// <param name="Hints">The last four characters of each secret, by the same name.</param>
/// <param name="Version">Grows with every replacement, so a cache of the opened secrets notices the change.</param>
public sealed record SealedIssuerSecrets(Dictionary<string, string> Ciphertexts, Dictionary<string, string> Hints, long Version)
{
    /// <summary>
    /// Never prints a ciphertext.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => $"{nameof(SealedIssuerSecrets)} {{ names: {string.Join(',', Ciphertexts.Keys)}, version: {Version} }}";
}

/// <summary>
/// The files of a validated document, stored in ms-filestorage.
/// </summary>
/// <param name="Xml">The signed XML of the document.</param>
/// <param name="AttachedDocument">The attached document the tax authority answers with.</param>
/// <param name="Pdf">The graphic representation.</param>
public sealed record DocumentFiles(StoredFile? Xml, StoredFile? AttachedDocument, StoredFile? Pdf);
