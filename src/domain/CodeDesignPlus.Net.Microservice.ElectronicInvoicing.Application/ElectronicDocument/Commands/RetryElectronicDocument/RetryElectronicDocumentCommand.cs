using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.RetryElectronicDocument;

/// <summary>
/// Retries an electronic document: a rejected one is corrected with the content sent and goes back to draft on the same
/// number; one still waiting is looked at now, and its retries start again if it was stalled; a validated one without its
/// XML asks for its files again.
/// </summary>
/// <param name="Id">The id of the document.</param>
/// <param name="Tenant">The tenant that issues; null for the tenant of the user.</param>
/// <param name="Content">The corrected content, required for a rejected document.</param>
/// <param name="PlatformIssuer">Whether the document is one of the platform's issuer; naming a tenant as well is refused (error 218).</param>
public record RetryElectronicDocumentCommand(Guid Id, Guid? Tenant, DocumentContentInput? Content, bool PlatformIssuer = false) : IRequest<IssuedDocumentDto>;

/// <summary>
/// Validates <see cref="RetryElectronicDocumentCommand"/>.
/// </summary>
public class Validator : AbstractValidator<RetryElectronicDocumentCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Content!).SetValidator(new DocumentContentInputValidator()).When(x => x.Content is not null);
    }
}

/// <summary>
/// Handles <see cref="RetryElectronicDocumentCommand"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="contents">Resolves the corrected content.</param>
/// <param name="user">The context of the caller.</param>
/// <param name="pubsub">The bus where the events are published.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class RetryElectronicDocumentCommandHandler(IElectronicDocumentRepository repository, IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IDocumentContentResolver contents, IUserContext user, IPubSub pubsub, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<RetryElectronicDocumentCommand, IssuedDocumentDto>
{
    /// <summary>
    /// Corrects or brings forward the document.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document as it stands.</returns>
    public async Task<IssuedDocumentDto> Handle(RetryElectronicDocumentCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var tenant = IssuerScope.TenantOf(request.Tenant, request.PlatformIssuer, user);
        var document = await repository.FindAsync<ElectronicDocumentAggregate>(request.Id, tenant, cancellationToken);

        ApplicationGuard.IsNull(document, Errors.ElectronicDocumentNotFound);

        // A validated document is only retried to ask again for the files it does not have.
        DomainGuard.IsTrue(document.Status == ElectronicDocumentStatus.Validated && !document.MissingFiles, DomainErrors.DocumentAlreadyValidated);

        var now = clock.GetCurrentInstant();
        var author = user.IdUser == Guid.Empty ? options.Value.SystemUser : user.IdUser;
        DocumentContent? corrected = null;

        if (document.Status == ElectronicDocumentStatus.Rejected)
        {
            DomainGuard.IsNull(request.Content, DomainErrors.ContentIsRequired);

            var issuer = await issuers.FindAsync<IssuerAggregate>(document.IssuerId, cancellationToken);

            ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

            corrected = await contents.ResolveAsync(issuer, document.Type, request.Content!, factory.Get(issuer.Provider).Capabilities, cancellationToken);

            // The correction is sent again from today: its due date cannot be before it.
            corrected.EnsureNotDueBefore(now.InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date);
        }

        ElectronicDocumentAggregate? saved = null;

        await repository.UpdateWithRetryAsync(
            () => repository.FindAsync<ElectronicDocumentAggregate>(request.Id, tenant, cancellationToken),
            fresh =>
            {
                saved = null;
                fresh.GetAndClearEvents();

                if (corrected is not null && fresh.Status == ElectronicDocumentStatus.Rejected)
                {
                    fresh.Correct(corrected, now, author);
                }
                else if (!fresh.Expedite(now, author))
                {
                    return false;
                }

                saved = fresh;

                return true;
            },
            cancellationToken);

        if (saved is null)
        {
            return IssuedDocumentDto.From(document);
        }

        var events = saved.GetAndClearEvents();

        // A document brought forward has no event of its own: the queued event sends it now instead of waiting for the job.
        await pubsub.PublishAsync(events.Count > 0 ? events : [ElectronicDocumentQueuedDomainEvent.Create(saved.Id, saved.Tenant, saved.Type, saved.Reference, saved.PrecededBy)], cancellationToken);

        return IssuedDocumentDto.From(saved);
    }
}
