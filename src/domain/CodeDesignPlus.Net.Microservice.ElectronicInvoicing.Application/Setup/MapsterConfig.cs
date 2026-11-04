using CodeDesignPlus.Microservice.Api.Dtos;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.CreateElectronicDocument;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Setup;

/// <summary>
/// Mappings between the aggregates, the commands and the DTOs of the service.
/// </summary>
public static class MapsterConfigElectronicInvoicing
{
    /// <summary>
    /// Registers the mappings in the global Mapster configuration.
    /// </summary>
    public static void Configure()
    {
        TypeAdapterConfig<CreateElectronicDocumentDto, CreateElectronicDocumentCommand>.NewConfig();

        // Field by field on purpose: a mapping by convention would publish by itself whatever a later member of the
        // aggregate is called, and the document will carry the buyer's data.
        TypeAdapterConfig<ElectronicDocumentAggregate, ElectronicDocumentDto>
            .NewConfig()
            .MapWith(src => new ElectronicDocumentDto
            {
                Id = src.Id,
                Tenant = src.Tenant,
                Reference = src.Reference,
                CreatedAt = src.CreatedAt
            });
    }
}
