using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.gRpc.Clients.Services.FileStorage;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Google.Protobuf;
using StoredFile = CodeDesignPlus.Net.ValueObjects.FileServer.File;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Files;

/// <summary>
/// Keeps the files of the validated documents in ms-filestorage through its gRPC, with their record and in the folder
/// <c>ElectronicInvoicing:FilesTarget</c> of the tenant.
/// </summary>
/// <remarks>
/// The id of each file derives from the document and the kind of file, so storing the files of the same document again
/// finds the same records instead of leaving copies. The files are signed by the system user of the configuration: they
/// are born in a worker, without anyone in session.
/// <para>
/// The platform's issuer is not a tenant (<see cref="PlatformScope"/>): ms-filestorage would refuse its reserved id as an
/// unknown tenant. Its files go without a tenant to <c>ElectronicInvoicing:PlatformIssuer:FilesTarget</c>, one of the
/// platform folders of ms-filestorage, which keeps them in the container of the platform.
/// </para>
/// </remarks>
/// <param name="storage">The gRPC client of ms-filestorage.</param>
/// <param name="options">The options of the service.</param>
public sealed class FileStorageDocumentFileStore(IFileStorageGrpc storage, IOptions<ElectronicInvoicingOptions> options) : IDocumentFileStore
{
    /// <summary>
    /// Namespace of the ids of the files derived from the document. Changing it would store the files again.
    /// </summary>
    private static readonly Guid IdNamespace = Guid.Parse("a1d4c7e2-8b35-4f69-9d0e-2c7b5a8f3e14");

    /// <inheritdoc/>
    public async Task<DocumentFiles> StoreAsync(Guid tenant, Guid documentId, ProviderFiles files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var xml = await StoreOneAsync(tenant, documentId, "xml", files.Xml, cancellationToken);
        var attached = await StoreOneAsync(tenant, documentId, "attached-document", files.AttachedDocument, cancellationToken);
        var pdf = await StoreOneAsync(tenant, documentId, "pdf", files.Pdf, cancellationToken);

        return new DocumentFiles(xml, attached, pdf);
    }

    /// <summary>
    /// The id of a file of a document: always the same for the same document and kind.
    /// </summary>
    /// <param name="documentId">The id of the document.</param>
    /// <param name="kind">The kind of file.</param>
    /// <returns>The deterministic id.</returns>
    public static Guid IdFor(Guid documentId, string kind) => DeterministicGuid.Create(IdNamespace, $"{documentId}|{kind}");

    private async Task<StoredFile?> StoreOneAsync(Guid tenant, Guid documentId, string kind, ProviderFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Content.Length == 0 || string.IsNullOrWhiteSpace(file.Name))
        {
            return null;
        }

        var id = IdFor(documentId, kind);
        var platform = PlatformScope.Owns(tenant);
        var target = platform ? options.Value.PlatformIssuer.FilesTarget : options.Value.FilesTarget;

        var response = await storage.UploadAsync(new UploadFileRequest
        {
            Id = id.ToString(),
            // ms-filestorage reads the tenant as a GUID and leaves the empty one without tenant context.
            Tenant = (platform ? Guid.Empty : tenant).ToString(),
            UploadedBy = options.Value.SystemUser.ToString(),
            Target = target,
            FileName = file.Name,
            Content = ByteString.CopyFrom(file.Content)
        }, cancellationToken);

        var name = string.IsNullOrWhiteSpace(response.FileName) ? file.Name : response.FileName;
        var described = new CodeDesignPlus.Net.File.Storage.Abstractions.Models.File(name);

        return StoredFile.Create(id, string.IsNullOrWhiteSpace(response.Target) ? target : response.Target, name, described.Extension ?? Path.GetExtension(name), described.ContentType, file.Content.LongLength);
    }
}
