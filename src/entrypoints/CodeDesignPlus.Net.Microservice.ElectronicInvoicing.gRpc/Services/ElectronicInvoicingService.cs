using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using Microsoft.AspNetCore.Authorization;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Services;

/// <summary>
/// The electronic invoicing contract other services call inside the cluster.
/// </summary>
/// <remarks>
/// <para>
/// Every method is anonymous because its callers (the order processing of ms-licenses, the issuing of ms-invoicing) are
/// workers and jobs without a user token, so the tenant travels in the request, as in <c>PaymentService</c> of
/// ms-payments. That is only safe while this gRPC endpoint is not published outside the cluster: its Service is a
/// ClusterIP and no VirtualService or Ingress may route to it. Publishing it would let anyone issue on behalf of any
/// tenant. A service identity for internal calls would replace this (Kappali pendings/740).
/// </para>
/// <para>
/// A document id reused with another content answers <c>ALREADY_EXISTS</c>; the provider or the Vault not answering,
/// <c>UNAVAILABLE</c>, so the caller retries; the rest of the errors go through the interceptor as
/// <c>FAILED_PRECONDITION</c> with their code.
/// </para>
/// </remarks>
/// <param name="mediator">Mediator that sends the commands and queries.</param>
public class ElectronicInvoicingService(IMediator mediator) : ElectronicInvoicing.ElectronicInvoicingBase
{
    /// <summary>
    /// Issues a document.
    /// </summary>
    /// <param name="request">The document.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The document waiting to be sent.</returns>
    [AllowAnonymous]
    public override Task<IssueDocumentResponse> IssueDocument(IssueDocumentRequest request, ServerCallContext context)
    {
        var command = new IssueElectronicDocumentCommand(
            GrpcMappings.GuidOf(request.Id),
            GrpcMappings.OptionalGuidOf(request.HasIssuerTenant, request.IssuerTenant),
            request.PlatformIssuer,
            GrpcMappings.TypeOf(request.Type),
            request.Reference,
            GrpcMappings.ToInput(request.Content),
            GrpcMappings.OptionalGuidOf(request.HasPrecededBy, request.PrecededBy));

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(command, context.CancellationToken)));
    }

    /// <summary>
    /// Gets a document of a tenant.
    /// </summary>
    /// <param name="request">The id of the document and its tenant.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The document.</returns>
    [AllowAnonymous]
    public override async Task<GetDocumentResponse> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        var document = await mediator.Send(new GetElectronicDocumentByIdQuery(GrpcMappings.GuidOf(request.Id), GrpcMappings.OptionalGuidOf(request.HasTenant, request.Tenant)), context.CancellationToken);

        return GrpcMappings.ToResponse(document);
    }

    /// <summary>
    /// Corrects a rejected document or looks at a waiting one now.
    /// </summary>
    /// <param name="request">The id, the tenant and, for a rejected document, the corrected content.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The document as it stands.</returns>
    [AllowAnonymous]
    public override Task<IssueDocumentResponse> RetryDocument(RetryDocumentRequest request, ServerCallContext context)
    {
        var command = new RetryElectronicDocumentCommand(GrpcMappings.GuidOf(request.Id), GrpcMappings.OptionalGuidOf(request.HasTenant, request.Tenant), request.Content is null ? null : GrpcMappings.ToInput(request.Content));

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(command, context.CancellationToken)));
    }

    /// <summary>
    /// Gets the issuer of a tenant with its capabilities and its quota.
    /// </summary>
    /// <param name="request">The tenant, or the platform's issuer.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The issuer; <c>registered = false</c> when the tenant has none.</returns>
    [AllowAnonymous]
    public override async Task<GetIssuerResponse> GetIssuer(GetIssuerRequest request, ServerCallContext context)
    {
        var issuer = await mediator.Send(new GetIssuerQuery(GrpcMappings.OptionalGuidOf(request.HasTenant, request.Tenant), request.PlatformIssuer), context.CancellationToken);

        return GrpcMappings.ToResponse(issuer);
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (CodeDesignPlusException exception) when (exception.Code == Application.Errors.DocumentIdReusedWithOtherContent.GetCode())
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"{exception.Code}: {exception.Message}"));
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsTransient(exception))
        {
            throw new RpcException(new Status(StatusCode.Unavailable, $"{exception.Code}: {exception.Message}"));
        }
    }
}
