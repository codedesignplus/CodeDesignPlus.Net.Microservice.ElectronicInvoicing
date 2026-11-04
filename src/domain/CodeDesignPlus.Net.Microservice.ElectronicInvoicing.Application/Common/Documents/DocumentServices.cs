using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;

/// <summary>
/// Keeps the files of the validated documents in ms-filestorage, with their record (the files are born in the backend,
/// so they go through its gRPC).
/// </summary>
public interface IDocumentFileStore
{
    /// <summary>
    /// Stores the files a provider handed over for a document.
    /// </summary>
    /// <param name="tenant">The tenant that owns the files.</param>
    /// <param name="documentId">The document they belong to; the ids of the files derive from it, so storing again does not duplicate.</param>
    /// <param name="files">The files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The references to the stored files; a file the provider did not hand over stays null.</returns>
    Task<DocumentFiles> StoreAsync(Guid tenant, Guid documentId, ProviderFiles files, CancellationToken cancellationToken);
}

/// <summary>
/// The failures of a dependency that the caller should retry later: the provider or the Vault did not answer.
/// </summary>
/// <remarks>
/// The entrypoints answer them as unavailable (503 in REST, <c>Unavailable</c> in gRPC) instead of a bad request, and
/// the status job retries them with its backoff.
/// </remarks>
public static class TransientFailures
{
    /// <summary>
    /// The codes of the failures that may succeed later.
    /// </summary>
    private static readonly HashSet<string> Codes = ["301", "303"];

    /// <summary>
    /// Whether the exception is a dependency that did not answer.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True when retrying later can succeed.</returns>
    public static bool IsTransient(CodeDesignPlusException exception) => Codes.Contains(exception.Code);

    /// <summary>
    /// Whether the exception says the provider rejected the credentials.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for a rejection of the credentials.</returns>
    public static bool IsCredentialsRejection(CodeDesignPlusException exception) => exception.Code == "302";
}
