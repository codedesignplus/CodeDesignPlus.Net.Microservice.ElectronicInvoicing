namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Core.Mapster;

/// <summary>
/// Mappings between the DTOs of the application and the gRPC messages.
/// </summary>
public static class MapsterConfig
{
    /// <summary>
    /// Registers the mappings in the global Mapster configuration.
    /// </summary>
    public static void Configure()
    {
        TypeAdapterConfig<ElectronicDocumentDto, GetDocumentResponse>
            .NewConfig()
            .MapWith(src => new GetDocumentResponse
            {
                Id = src.Id.ToString(),
                Tenant = src.Tenant.ToString(),
                Reference = src.Reference
            });
    }
}
