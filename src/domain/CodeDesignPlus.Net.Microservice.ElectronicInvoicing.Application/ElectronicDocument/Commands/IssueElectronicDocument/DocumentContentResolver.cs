using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;

/// <summary>
/// Turns what a caller sends into the content a document keeps, checking it against what the provider of the issuer
/// can do.
/// </summary>
public interface IDocumentContentResolver
{
    /// <summary>
    /// Resolves the content of a document of an issuer.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="type">The type of the document.</param>
    /// <param name="input">What the caller sent.</param>
    /// <param name="capabilities">What the provider of the issuer can do.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content.</returns>
    Task<DocumentContent> ResolveAsync(IssuerAggregate issuer, ElectronicDocumentType type, DocumentContentInput input, ProviderCapabilities capabilities, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the decimal digits of the currency with ms-locations (the backend is the authority on them) and, in a note
/// on a document issued here, the number, the CUFE and the issue date of that document.
/// </summary>
/// <param name="currencies">The currencies of ms-locations.</param>
/// <param name="documents">The documents of this service.</param>
public sealed class DocumentContentResolver(ICurrencyGrpc currencies, IElectronicDocumentRepository documents) : IDocumentContentResolver
{
    /// <inheritdoc/>
    public async Task<DocumentContent> ResolveAsync(IssuerAggregate issuer, ElectronicDocumentType type, DocumentContentInput input, ProviderCapabilities capabilities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(capabilities);

        DomainGuard.IsFalse(capabilities.DocumentTypes.Contains(type), DomainErrors.DocumentTypeNotSupported);
        DomainGuard.IsTrue((input.Prepayments?.Count ?? 0) > 0 && !capabilities.SupportsPrepayments, DomainErrors.DocumentTypeNotSupported);

        var digits = await DecimalDigitsOfAsync(input.Totals?.Currency, cancellationToken);
        var reference = await ReferenceOfAsync(issuer, type, input.BillingReference, capabilities, cancellationToken);

        return input.ToContent(digits, reference);
    }

    private async Task<short> DecimalDigitsOfAsync(string? code, CancellationToken cancellationToken)
    {
        DomainGuard.IsTrue(string.IsNullOrWhiteSpace(code) || code.Length != 3, DomainErrors.CurrencyIsInvalid);

        try
        {
            var currency = await currencies.GetCurrencyAsync(code: code!.ToUpperInvariant(), cancellationToken: cancellationToken);

            ApplicationGuard.IsNull(currency, Errors.CurrencyNotFound);

            return currency.DecimalDigits;
        }
        catch (Grpc.Core.RpcException exception) when (exception.StatusCode is Grpc.Core.StatusCode.NotFound or Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.InvalidArgument)
        {
            throw ApplicationErrors.Of(Errors.CurrencyNotFound);
        }
    }

    private async Task<BillingReference?> ReferenceOfAsync(IssuerAggregate issuer, ElectronicDocumentType type, BillingReferenceInput? input, ProviderCapabilities capabilities, CancellationToken cancellationToken)
    {
        if (!DocumentContent.IsNote(type))
        {
            // The domain refuses a reference outside a note: it is passed on so it says so.
            return input is null ? null : new BillingReference(input.DocumentId, input.Prefix ?? string.Empty, input.Consecutive ?? 0, input.Cufe ?? string.Empty, input.IssueDate ?? default);
        }

        DomainGuard.IsNull(input, DomainErrors.NoteWithoutBillingReference);

        if (input!.DocumentId is { } documentId && documentId != Guid.Empty)
        {
            var corrected = await documents.FindAsync<ElectronicDocumentAggregate>(documentId, issuer.Tenant, cancellationToken);

            // A note corrects only the kind of document it was made for: a ticket is not corrected with a credit note.
            ApplicationGuard.IsTrue(corrected is null || corrected.Type != DocumentContent.CorrectedTypeOf(type) || corrected.Status != ElectronicDocumentStatus.Validated || corrected.Number is null || corrected.Cufe is null, Errors.BillingReferenceNotFound);

            return new BillingReference(documentId, corrected!.Number!.Prefix, corrected.Number.Consecutive, corrected.Cufe!, corrected.IssueDate ?? corrected.ValidatedAt!.Value.InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date);
        }

        ApplicationGuard.IsFalse(capabilities.ExternalBillingReference, Errors.ExternalBillingReferenceNotSupported);
        DomainGuard.IsTrue(input.Prefix is null || input.Consecutive is null || string.IsNullOrWhiteSpace(input.Cufe) || input.IssueDate is null, DomainErrors.NoteWithoutBillingReference);

        return new BillingReference(null, input.Prefix!, input.Consecutive!.Value, input.Cufe!, input.IssueDate!.Value);
    }
}
