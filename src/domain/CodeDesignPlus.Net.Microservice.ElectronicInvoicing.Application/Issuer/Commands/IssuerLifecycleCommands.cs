using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

/// <summary>
/// Changes the provider the issuer of the tenant of the user issues with, before it exists in a provider.
/// </summary>
/// <param name="Provider">The code of the provider; it must be allowed in the country of the issuer.</param>
public record ChooseIssuerProviderCommand(string Provider) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="ChooseIssuerProviderCommand"/>.
/// </summary>
public class ChooseIssuerProviderValidator : AbstractValidator<ChooseIssuerProviderCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public ChooseIssuerProviderValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="ChooseIssuerProviderCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
/// <param name="options">The options of the service.</param>
public class ChooseIssuerProviderCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IPubSub pubsub, IUserContext user, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<ChooseIssuerProviderCommand>
{
    /// <summary>
    /// Changes the provider.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task Handle(ChooseIssuerProviderCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        return IssuerWrites.UpdateAsync(issuers, pubsub, tenant, issuer => issuer.ChooseProvider(IssuerWrites.AllowedProvider(options.Value, factory, issuer.Country, request.Provider).Code, user.IdUser), cancellationToken);
    }
}

/// <summary>
/// Records the mandate the issuer of the tenant of the user accepted for this service to issue on its behalf.
/// </summary>
/// <param name="Version">The version of the text accepted.</param>
/// <param name="File">The signed document, already stored in ms-filestorage.</param>
public record AcceptIssuerMandateCommand(string Version, StoredFile? File) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="AcceptIssuerMandateCommand"/>.
/// </summary>
public class AcceptIssuerMandateValidator : AbstractValidator<AcceptIssuerMandateCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public AcceptIssuerMandateValidator()
    {
        RuleFor(x => x.Version).NotEmpty().MaximumLength(FieldLength.Name);
    }
}

/// <summary>
/// Handles <see cref="AcceptIssuerMandateCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
/// <param name="clock">The clock.</param>
public class AcceptIssuerMandateCommandHandler(IIssuerRepository issuers, IPubSub pubsub, IUserContext user, IClock clock) : IRequestHandler<AcceptIssuerMandateCommand>
{
    /// <summary>
    /// Records the mandate, signed by the user.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task Handle(AcceptIssuerMandateCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var mandate = new IssuerMandate(request.Version, clock.GetCurrentInstant(), user.IdUser, request.File);

        return IssuerWrites.UpdateAsync(issuers, pubsub, tenant, issuer => issuer.AcceptMandate(mandate, user.IdUser), cancellationToken);
    }
}

/// <summary>
/// Starts the enablement tests of the issuer of the tenant of the user with the test set the tax authority gave.
/// </summary>
/// <param name="TestSetId">The id of the test set.</param>
/// <param name="DocumentType">
/// The type of document the test set enables, for the providers that send the test set on their own; null is the
/// invoice. The tax authority gives a test set for the invoices (with their notes) and another for the point of sale.
/// </param>
public record StartIssuerTestingCommand(string TestSetId, ElectronicDocumentType? DocumentType = null) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="StartIssuerTestingCommand"/>.
/// </summary>
public class StartIssuerTestingValidator : AbstractValidator<StartIssuerTestingCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public StartIssuerTestingValidator()
    {
        RuleFor(x => x.TestSetId).NotEmpty();
        RuleFor(x => x.DocumentType).IsInEnum().When(x => x.DocumentType is not null);
    }
}

/// <summary>
/// Handles <see cref="StartIssuerTestingCommand"/>.
/// </summary>
/// <remarks>
/// A provider that sends the test set on its own (<see cref="IProviderTestSetRunner"/>) is asked to send it first, and
/// the issuer enters its tests only if the provider did not reject it. An issuer already enabled that enables one more
/// type of document (the point of sale after the invoices) keeps its status: its other documents keep going. With the
/// other providers the documents of the set go through this service, with the environment of the tests.
/// </remarks>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="resolver">Prepares the issuer for the adapter.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
/// <param name="logger">The logger.</param>
public class StartIssuerTestingCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IProviderIssuerResolver resolver, IPubSub pubsub, IUserContext user, ILogger<StartIssuerTestingCommandHandler> logger) : IRequestHandler<StartIssuerTestingCommand>
{
    /// <summary>
    /// Starts the enablement tests, when the provider sends the test set through its API.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(StartIssuerTestingCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);
        ApplicationGuard.IsFalse(factory.Get(issuer.Provider).Capabilities.HabilitationByApi, Errors.HabilitationNotByApi);

        var runner = factory.FindTestSetRunner(issuer.Provider);

        if (runner is not null)
        {
            var testSet = await runner.RunTestSetAsync(await resolver.ResolveAsync(issuer, cancellationToken), request.DocumentType ?? ElectronicDocumentType.Invoice, request.TestSetId, cancellationToken);

            logger.LogInformation("The test set of issuer {IssuerId} for {DocumentType} is {Status} in its provider.", issuer.Id, request.DocumentType ?? ElectronicDocumentType.Invoice, testSet.Raw);

            ApplicationGuard.IsTrue(testSet.Status == ProviderDocumentStatus.Rejected, Errors.TestSetRejected);

            if (issuer.Status == IssuerStatus.Enabled)
            {
                return;
            }
        }

        await IssuerWrites.UpdateAsync(issuers, pubsub, tenant, x => x.StartTesting(request.TestSetId, user.IdUser), cancellationToken);
    }
}

/// <summary>
/// Enables the issuer of the tenant of the user to send to an environment.
/// </summary>
/// <param name="Environment">Where its documents go from now on: tests or production.</param>
public record EnableIssuerCommand(IssuerEnvironment Environment) : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Validates <see cref="EnableIssuerCommand"/>.
/// </summary>
public class EnableIssuerValidator : AbstractValidator<EnableIssuerCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public EnableIssuerValidator()
    {
        RuleFor(x => x.Environment).IsInEnum();
    }
}

/// <summary>
/// Handles <see cref="EnableIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
public class EnableIssuerCommandHandler(IIssuerRepository issuers, IPubSub pubsub, IUserContext user) : IRequestHandler<EnableIssuerCommand>
{
    /// <summary>
    /// Enables the issuer and announces it.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task Handle(EnableIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        return IssuerWrites.UpdateAsync(issuers, pubsub, tenant, issuer => issuer.Enable(request.Environment, user.IdUser), cancellationToken);
    }
}

/// <summary>
/// Suspends the issuer of the tenant of the user: it does not issue until it is enabled again.
/// </summary>
public record SuspendIssuerCommand : IRequest
{
    /// <summary>
    /// Whether it works with the platform's issuer instead of the one of the tenant of the user. The endpoint sets it; the
    /// body of the request cannot.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool PlatformIssuer { get; init; }
}

/// <summary>
/// Handles <see cref="SuspendIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
public class SuspendIssuerCommandHandler(IIssuerRepository issuers, IPubSub pubsub, IUserContext user) : IRequestHandler<SuspendIssuerCommand>
{
    /// <summary>
    /// Suspends the issuer and announces it.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task Handle(SuspendIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.PlatformIssuer, user);

        return IssuerWrites.UpdateAsync(issuers, pubsub, tenant, issuer => issuer.Suspend(Errors.IssuerSuspendedByUser.GetCode(), user.IdUser), cancellationToken);
    }
}
