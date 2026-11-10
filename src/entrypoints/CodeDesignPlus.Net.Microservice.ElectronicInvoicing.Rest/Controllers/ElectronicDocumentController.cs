namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The electronic documents of the tenant of the session. They are issued by the services that originate them, through
/// gRPC; here they are read and retried.
/// </summary>
/// <remarks>
/// The XML and the PDF are references to ms-filestorage in each document (<c>xml</c>, <c>pdf</c>,
/// <c>attachedDocument</c>): they are downloaded there, with its permissions.
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
[Route("api/[controller]")]
[ApiController]
public class ElectronicDocumentController(IMediator mediator) : ElectronicDocumentControllerBase(mediator, false)
{
}
