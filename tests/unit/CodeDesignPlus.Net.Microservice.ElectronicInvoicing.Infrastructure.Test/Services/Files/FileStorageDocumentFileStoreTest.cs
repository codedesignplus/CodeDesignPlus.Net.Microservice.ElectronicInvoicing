using CodeDesignPlus.Net.gRpc.Clients.Abstractions;
using CodeDesignPlus.Net.gRpc.Clients.Services.FileStorage;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Files;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Files;

/// <summary>
/// Where the files of a validated document are kept in ms-filestorage.
/// </summary>
public class FileStorageDocumentFileStoreTest
{
    private static readonly Guid Document = Guid.Parse("cc000000-0000-4000-8000-000000000001");

    private readonly Mock<IFileStorageGrpc> storage = new();

    private readonly List<UploadFileRequest> uploads = [];

    public FileStorageDocumentFileStoreTest()
    {
        storage
            .Setup(x => x.UploadAsync(It.IsAny<UploadFileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UploadFileRequest, CancellationToken>((request, _) => uploads.Add(request))
            .ReturnsAsync((UploadFileRequest request, CancellationToken _) => new UploadFileResponse { FileName = request.FileName, Target = request.Target });
    }

    private FileStorageDocumentFileStore Store() => new(storage.Object, Microsoft.Extensions.Options.Options.Create(new ElectronicInvoicingOptions
    {
        FilesTarget = "electronic-documents",
        PlatformIssuer = new PlatformIssuerOptions { ReservedDocuments = 1, FilesTarget = "platform-electronic-documents" },
        SystemUser = Guid.Parse("dd000000-0000-4000-8000-000000000001")
    }));

    private static ProviderFiles Files() => new(new ProviderFile("SETP1.xml", [1, 2, 3]), null, new ProviderFile("SETP1.pdf", [4, 5]));

    [Fact]
    public async Task StoreAsync_PlatformIssuer_UploadsWithoutTenantToThePlatformFolder()
    {
        // Act
        var stored = await Store().StoreAsync(PlatformScope.Tenant, Document, Files(), CancellationToken.None);

        // Assert
        Assert.Equal((2, Guid.Empty.ToString(), "platform-electronic-documents", "platform-electronic-documents"), (uploads.Count, uploads.Select(x => x.Tenant).Distinct().Single(), uploads.Select(x => x.Target).Distinct().Single(), stored.Xml!.Target));
    }

    [Fact]
    public async Task StoreAsync_Tenant_UploadsUnderTheTenantToItsFolder()
    {
        // Arrange
        var tenant = Guid.Parse("ee000000-0000-4000-8000-000000000001");

        // Act
        await Store().StoreAsync(tenant, Document, Files(), CancellationToken.None);

        // Assert
        Assert.Equal((tenant.ToString(), "electronic-documents"), (uploads.Select(x => x.Tenant).Distinct().Single(), uploads.Select(x => x.Target).Distinct().Single()));
    }
}
