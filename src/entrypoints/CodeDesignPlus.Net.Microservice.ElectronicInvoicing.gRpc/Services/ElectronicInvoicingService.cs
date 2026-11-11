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
/// The issuer is decided only by the request (<see cref="ScopeOf"/>): a tenant is the issuer of that tenant, no tenant
/// is the platform's issuer, and both at once are refused. The tenant of the caller's context (an <c>X-Tenant</c> header
/// a client might propagate) is never read here, so a worker running for a tenant cannot issue as that tenant by
/// forgetting to name it, nor as the platform by naming it.
/// </para>
/// <para>
/// A document id reused with another content answers <c>ALREADY_EXISTS</c>; a scope that is ambiguous or names the
/// platform's reserved tenant, or a caller's number that is incomplete or has an invalid prefix, <c>INVALID_ARGUMENT</c>; the provider or the Vault not answering,
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
        var (tenant, platformIssuer) = ScopeOf(request.HasIssuerTenant, request.IssuerTenant, request.PlatformIssuer);
        var command = new IssueElectronicDocumentCommand(
            GrpcMappings.GuidOf(request.Id),
            tenant,
            platformIssuer,
            GrpcMappings.TypeOf(request.Type),
            request.Reference,
            GrpcMappings.ToInput(request.Content),
            GrpcMappings.OptionalGuidOf(request.HasPrecededBy, request.PrecededBy))
        {
            Prefix = request.HasPrefix ? request.Prefix : null,
            Consecutive = request.HasConsecutive ? request.Consecutive : null
        };

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(command, context.CancellationToken)));
    }

    /// <summary>
    /// Gets a document of a tenant or of the platform's issuer.
    /// </summary>
    /// <param name="request">The id of the document and its tenant, or none for the platform's issuer.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The document.</returns>
    [AllowAnonymous]
    public override Task<GetDocumentResponse> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        var (tenant, platformIssuer) = ScopeOf(request.HasTenant, request.Tenant, request.PlatformIssuer);

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(new GetElectronicDocumentByIdQuery(GrpcMappings.GuidOf(request.Id), tenant, platformIssuer), context.CancellationToken)));
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
        var (tenant, platformIssuer) = ScopeOf(request.HasTenant, request.Tenant, request.PlatformIssuer);
        var command = new RetryElectronicDocumentCommand(GrpcMappings.GuidOf(request.Id), tenant, request.Content is null ? null : GrpcMappings.ToInput(request.Content), platformIssuer);

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(command, context.CancellationToken)));
    }

    /// <summary>
    /// Gets the issuer of a tenant with its capabilities and its quota.
    /// </summary>
    /// <param name="request">The tenant, or none for the platform's issuer.</param>
    /// <param name="context">The context of the call.</param>
    /// <returns>The issuer; <c>registered = false</c> when the tenant has none.</returns>
    [AllowAnonymous]
    public override Task<GetIssuerResponse> GetIssuer(GetIssuerRequest request, ServerCallContext context)
    {
        var (tenant, platformIssuer) = ScopeOf(request.HasTenant, request.Tenant, request.PlatformIssuer);

        return CallAsync(async () => GrpcMappings.ToResponse(await mediator.Send(new GetIssuerQuery(tenant, platformIssuer), context.CancellationToken)));
    }

    /// <summary>
    /// The scope of a request: the tenant it names, or the platform's issuer when it names none.
    /// </summary>
    /// <param name="hasTenant">Whether the request carries the tenant.</param>
    /// <param name="tenant">The tenant it carries.</param>
    /// <param name="platformIssuer">Whether it asks for the platform's issuer.</param>
    /// <returns>The tenant named, and whether the platform's issuer is asked for (also when no tenant is named).</returns>
    /// <remarks>Both at once go through as they came: the application refuses them with error 218 instead of guessing.</remarks>
    public static (Guid? Tenant, bool PlatformIssuer) ScopeOf(bool hasTenant, string tenant, bool platformIssuer)
    {
        var named = GrpcMappings.OptionalGuidOf(hasTenant, tenant);

        return (named, platformIssuer || named is null);
    }

    /// <summary>
    /// The errors that say the request itself is malformed, whatever the state of the issuer: an ambiguous scope, the
    /// reserved tenant, a number with its prefix or its consecutive alone, or a prefix that cannot be one.
    /// </summary>
    private static readonly HashSet<string> InvalidArguments =
    [
        Application.Errors.PlatformIssuerWithTenant.GetCode(),
        Application.Errors.TenantIsReserved.GetCode(),
        Application.Errors.CallerNumberIsIncomplete.GetCode(),
        Domain.Errors.PrefixIsInvalid.GetCode()
    ];

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
        catch (CodeDesignPlusException exception) when (InvalidArguments.Contains(exception.Code))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"{exception.Code}: {exception.Message}"));
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsTransient(exception))
        {
            throw new RpcException(new Status(StatusCode.Unavailable, $"{exception.Code}: {exception.Message}"));
        }
    }
}
