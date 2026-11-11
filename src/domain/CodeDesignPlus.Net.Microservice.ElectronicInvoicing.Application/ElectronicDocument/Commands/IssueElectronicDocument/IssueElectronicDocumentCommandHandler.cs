using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Quotas;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;

/// <summary>
/// Handles <see cref="IssueElectronicDocumentCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same id is the retry of the same request: with the same content it returns the document as it is, before any
/// other check (the first request already used the quota and the number); with another content it is error 201.
/// </para>
/// <para>
/// A document is not received due before today in the time zone of the issuer (<c>152</c>): the provider puts the issue
/// date when it receives it, so a due date already gone by is refused here and not by the tax authority.
/// </para>
/// <para>
/// In order: the issuer and its provider are checked, the content is resolved, a range is chosen (when this service
/// numbers), the quota counts the document, the document is saved, the number is taken and the events are published.
/// Two requests at once with the same id both pass the first check; the second one hits the id, gives back its unit of
/// the quota and answers as a retry. The number is taken after the document exists, so a request that loses that race
/// does not burn a number.
/// </para>
/// <para>
/// A caller that keeps its own sequence sends the number (prefix and consecutive together, or neither: <c>220</c>).
/// Nothing is reserved then: the number must fall in a range of the issuer for the type (<c>221</c>) valid today in its
/// time zone (<c>222</c>), unless the issuer has no range for the type, and no other document of the issuer and type may
/// carry it (<c>223</c>). The unique index of the numbers decides between two requests at once. A provider that numbers
/// by itself takes no number from the caller (<c>224</c>).
/// </para>
/// </remarks>
/// <param name="repository">The repository of the documents.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="contents">Resolves the content.</param>
/// <param name="numbering">Chooses the range and takes the number.</param>
/// <param name="quotas">Counts the documents against the quota.</param>
/// <param name="user">The context of the caller.</param>
/// <param name="pubsub">The bus where the events are published.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
/// <param name="logger">The logger.</param>
public class IssueElectronicDocumentCommandHandler(
    IElectronicDocumentRepository repository,
    IIssuerRepository issuers,
    IElectronicInvoiceProviderFactory factory,
    IDocumentContentResolver contents,
    INumberingService numbering,
    IQuotaGate quotas,
    IUserContext user,
    IPubSub pubsub,
    IClock clock,
    IOptions<ElectronicInvoicingOptions> options,
    ILogger<IssueElectronicDocumentCommandHandler> logger) : IRequestHandler<IssueElectronicDocumentCommand, IssuedDocumentDto>
{
    /// <summary>
    /// Issues the document unless a previous request with the same id already did.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document waiting to be sent.</returns>
    public async Task<IssuedDocumentDto> Handle(IssueElectronicDocumentCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var callerNumber = CallerNumberOf(request);
        var tenant = IssuerScope.TenantOf(request.Tenant, request.PlatformIssuer, user);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        var provider = factory.Get(issuer.Provider);

        var existing = await repository.FindAsync<ElectronicDocumentAggregate>(request.Id, cancellationToken);

        if (existing is not null)
        {
            return await AnswerRetryAsync(existing, issuer, provider, request, callerNumber, cancellationToken);
        }

        DomainGuard.IsNull(issuer.Mandate, DomainErrors.MandateNotAccepted);
        DomainGuard.IsFalse(issuer.CanIssue, DomainErrors.IssuerNotEnabled);

        if (request.PrecededBy is { } preceding && preceding != Guid.Empty)
        {
            ApplicationGuard.IsFalse(await repository.ExistsAsync<ElectronicDocumentAggregate>(preceding, tenant, cancellationToken), Errors.PrecedingDocumentNotFound);
        }

        var content = await contents.ResolveAsync(issuer, request.Type, request.Content, provider.Capabilities, cancellationToken);
        var now = clock.GetCurrentInstant();
        var today = now.InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date;

        // Checked only here and not on a retry of the same id: a retry answers with the document already received, even
        // after its due date went by.
        content.EnsureNotDueBefore(today);

        var document = ElectronicDocumentAggregate.Create(request.Id, tenant, issuer.Id, request.Type, request.Reference, content, request.PrecededBy, now, user.IdUser == Guid.Empty ? options.Value.SystemUser : user.IdUser);

        NumberingRangeAggregate? range = null;

        if (callerNumber is not null)
        {
            await AdoptCallerNumberAsync(document, issuer, provider, callerNumber, today, cancellationToken);
        }
        else if (provider.Capabilities.Numbering == NumberingMode.ByPlatform)
        {
            range = await numbering.SelectAsync(tenant, request.Type, today, cancellationToken);
        }

        var quotaEvents = await quotas.ReserveAsync(tenant, request.Type, now, cancellationToken);

        if (!await repository.CreateIfAbsentAsync(document, cancellationToken))
        {
            await quotas.ReleaseAsync(tenant, now, cancellationToken);

            var winner = await repository.FindAsync<ElectronicDocumentAggregate>(request.Id, cancellationToken);

            // No document with the id: what was already there is the number, saved by another request at the same time.
            ApplicationGuard.IsTrue(winner is null && callerNumber is not null, Errors.CallerNumberAlreadyUsed);

            return await AnswerRetryAsync(winner, issuer, provider, request, callerNumber, cancellationToken);
        }

        var events = document.GetAndClearEvents().ToList();

        if (range is not null)
        {
            document = await AssignNumberAsync(document, range.Id, today, cancellationToken) ?? document;
        }

        await pubsub.PublishAsync([.. events, .. quotaEvents], cancellationToken);

        return IssuedDocumentDto.From(document);
    }

    /// <summary>
    /// The number the caller gives, when it gives one: the prefix and the consecutive go together.
    /// </summary>
    private static DocumentNumber? CallerNumberOf(IssueElectronicDocumentCommand request)
    {
        if (request.Prefix is null && request.Consecutive is null)
        {
            return null;
        }

        ApplicationGuard.IsTrue(request.Prefix is null || request.Consecutive is null || request.Consecutive <= 0, Errors.CallerNumberIsIncomplete);
        DomainGuard.IsFalse(NumberingRangeAggregate.IsValidPrefix(request.Prefix), DomainErrors.PrefixIsInvalid);

        return new DocumentNumber(request.Prefix!, request.Consecutive!.Value);
    }

    /// <summary>
    /// Gives the document the number of the caller: inside a range of the issuer valid today, when it has ranges for the
    /// type, and used by no other document. Nothing is reserved: the caller keeps its own sequence.
    /// </summary>
    private async Task AdoptCallerNumberAsync(ElectronicDocumentAggregate document, IssuerAggregate issuer, IElectronicInvoiceProvider provider, DocumentNumber number, LocalDate today, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsTrue(provider.Capabilities.Numbering != NumberingMode.ByPlatform, Errors.CallerNumberNotAccepted);

        var range = await numbering.MatchAsync(issuer.Tenant, document.Type, number, today, cancellationToken);

        // The unique index decides between two requests at once; this only says it earlier and without counting the quota.
        ApplicationGuard.IsTrue(await repository.IsNumberTakenAsync(issuer.Id, document.Type, number, cancellationToken), Errors.CallerNumberAlreadyUsed);

        document.AssignNumber(number, range?.Id);
    }

    private async Task<ElectronicDocumentAggregate?> AssignNumberAsync(ElectronicDocumentAggregate document, Guid rangeId, LocalDate today, CancellationToken cancellationToken)
    {
        try
        {
            var number = await numbering.ReserveAsync(rangeId, today, cancellationToken);
            ElectronicDocumentAggregate? numbered = null;

            await repository.UpdateWithRetryAsync(
                () => repository.FindAsync<ElectronicDocumentAggregate>(document.Id, cancellationToken),
                fresh =>
                {
                    numbered = fresh;

                    return fresh.AssignNumber(number, rangeId);
                },
                cancellationToken);

            return numbered;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The document exists and waits in Draft: the one that sends it takes the number before sending.
            logger.LogWarning("The number of electronic document {DocumentId} was not taken when it was created: {Error}", document.Id, exception.GetType().Name);

            return null;
        }
    }

    private async Task<IssuedDocumentDto> AnswerRetryAsync(ElectronicDocumentAggregate? existing, IssuerAggregate issuer, IElectronicInvoiceProvider provider, IssueElectronicDocumentCommand request, DocumentNumber? callerNumber, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(existing, Errors.ElectronicDocumentNotFound);
        ApplicationGuard.IsTrue(existing.Tenant != issuer.Tenant || existing.Type != request.Type, Errors.DocumentIdReusedWithOtherContent);
        ApplicationGuard.IsTrue(callerNumber is not null && callerNumber != existing.Number, Errors.DocumentIdReusedWithOtherContent);

        var content = await contents.ResolveAsync(issuer, request.Type, request.Content, provider.Capabilities, cancellationToken);
        var hash = ElectronicDocumentAggregate.ContentHashOf(request.Type, request.Reference, request.PrecededBy is { } preceding && preceding != Guid.Empty ? preceding : null, content);

        ApplicationGuard.IsTrue(hash != existing.ContentHash, Errors.DocumentIdReusedWithOtherContent);

        return IssuedDocumentDto.From(existing);
    }
}
