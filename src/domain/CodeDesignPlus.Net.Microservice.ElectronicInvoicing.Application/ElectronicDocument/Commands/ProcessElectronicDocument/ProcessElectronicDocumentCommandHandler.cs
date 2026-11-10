using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Documents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Numbering;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;

/// <summary>
/// Handles <see cref="ProcessElectronicDocumentCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// A round first claims the document (it moves its next look to the end of a lease, checking the version it read): the
/// one that saves the claim is the only one that talks to the provider, and a document whose next look is not due yet is
/// left alone. Every change after the call to the provider is saved only if nobody wrote the document since the claim.
/// </para>
/// <para>
/// A draft waits while the document that precedes it is not validated or its issuer cannot issue. Before sending again
/// a draft whose previous send may have arrived, a provider that finds documents by their number is asked first, and
/// what it has is adopted instead of sending twice (a rejection found that way is not adopted: it may be the one the
/// correction came to fix).
/// </para>
/// <para>
/// The tax authority not answering is not a rejection: the document stays sent, in contingency, and keeps being asked
/// about; the provider retries on its own. After the configured attempts, or too long sent without a final answer, the
/// document is announced as stalled and left until someone retries it. Each round leaves its outcome on the document.
/// </para>
/// <para>
/// A validated document whose XML could not be kept in ms-filestorage is still due: the issuer must keep it, so its files
/// are asked for again with the same backoff and, after the same attempts, it is announced and left until someone
/// retries it. Asking again is safe: the ids of the files derive from the document.
/// </para>
/// </remarks>
/// <param name="repository">The repository of the documents.</param>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="resolver">Opens the credentials of the issuer.</param>
/// <param name="numbering">Takes the number of a document.</param>
/// <param name="files">Stores the files in ms-filestorage.</param>
/// <param name="pubsub">The bus where the events are published.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
/// <param name="logger">The logger.</param>
public class ProcessElectronicDocumentCommandHandler(
    IElectronicDocumentRepository repository,
    IIssuerRepository issuers,
    IElectronicInvoiceProviderFactory factory,
    IProviderIssuerResolver resolver,
    INumberingService numbering,
    IDocumentFileStore files,
    IPubSub pubsub,
    IClock clock,
    IOptions<ElectronicInvoicingOptions> options,
    ILogger<ProcessElectronicDocumentCommandHandler> logger) : IRequestHandler<ProcessElectronicDocumentCommand, ProcessOutcome>
{
    /// <summary>
    /// Runs one round of the document.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the round did.</returns>
    public async Task<ProcessOutcome> Handle(ProcessElectronicDocumentCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var now = clock.GetCurrentInstant();
        var document = await ClaimAsync(request.Id, now, cancellationToken);

        if (document is null)
        {
            return ProcessOutcome.Skipped;
        }

        var issuer = await issuers.FindAsync<IssuerAggregate>(document.IssuerId, cancellationToken);
        var provider = issuer is null ? null : factory.Find(issuer.Provider);

        if (document.Status == ElectronicDocumentStatus.Validated)
        {
            return await KeepFilesAsync(document, issuer, provider, now, cancellationToken);
        }

        if (issuer is null || provider is null)
        {
            return await WaitAsync(document, now, "issuer-or-provider-missing", cancellationToken);
        }

        if (document.Status == ElectronicDocumentStatus.Draft)
        {
            var reason = await ReasonToWaitAsync(document, issuer, cancellationToken);

            if (reason is not null)
            {
                return await WaitAsync(document, now, reason, cancellationToken);
            }

            if (provider.Capabilities.Numbering == NumberingMode.ByPlatform && document.Number is null)
            {
                var numbered = await NumberAsync(document, issuer, now, cancellationToken);

                if (numbered is null)
                {
                    return ProcessOutcome.Skipped;
                }

                if (numbered.Number is null)
                {
                    return await WaitAsync(numbered, now, "no-number", cancellationToken);
                }

                document = numbered;
            }
        }

        try
        {
            var outcome = document.Status == ElectronicDocumentStatus.Draft
                ? await SendAsync(document, issuer, provider, now, cancellationToken)
                : await CheckAsync(document, issuer, provider, now, cancellationToken);

            await ForgetCredentialRejectionsAsync(issuer, outcome, cancellationToken);

            return outcome;
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsTransient(exception))
        {
            logger.LogWarning("Electronic document {DocumentId}: a dependency did not answer ({Code}); it is retried later.", document.Id, exception.Code);

            var outcome = await WaitAsync(document, now, "unavailable", cancellationToken);

            return outcome == ProcessOutcome.Stalled ? outcome : ProcessOutcome.Deferred;
        }
        catch (CodeDesignPlusException exception) when (TransientFailures.IsIssuerRejection(exception))
        {
            logger.LogWarning("Electronic document {DocumentId}: the provider does not let issuer {IssuerId} issue ({Code}).", document.Id, issuer.Id, exception.Code);

            await RecordCredentialRejectionAsync(issuer, exception.Code, cancellationToken);

            var outcome = await WaitAsync(document, now, TransientFailures.IsCredentialsRejection(exception) ? "credentials-rejected" : "issuer-not-ready", cancellationToken);

            return outcome == ProcessOutcome.Stalled ? outcome : ProcessOutcome.Deferred;
        }
    }

    private async Task<string?> ReasonToWaitAsync(ElectronicDocumentAggregate document, IssuerAggregate issuer, CancellationToken cancellationToken)
    {
        if (document.PrecededBy is { } precededBy)
        {
            var preceding = await repository.FindAsync<ElectronicDocumentAggregate>(precededBy, cancellationToken);

            if (preceding?.Status != ElectronicDocumentStatus.Validated)
            {
                return "waiting-preceding";
            }
        }

        return issuer.CanIssue ? null : "issuer-not-enabled";
    }

    private async Task<ElectronicDocumentAggregate?> NumberAsync(ElectronicDocumentAggregate document, IssuerAggregate issuer, Instant now, CancellationToken cancellationToken)
    {
        try
        {
            var today = now.InZone(DateTimeZoneProviders.Tzdb[issuer.TimeZoneId]).Date;
            var selected = await numbering.SelectAsync(document.Tenant, document.Type, today, cancellationToken);
            var number = await numbering.ReserveAsync(selected.Id, today, cancellationToken);

            return await SaveAsync(document, x => x.AssignNumber(number, selected.Id), cancellationToken);
        }
        catch (CodeDesignPlusException exception) when (!TransientFailures.IsTransient(exception))
        {
            // No range can number it today (exhausted, expired or missing): it waits until someone registers one, and
            // stalls if nobody does.
            logger.LogWarning("Electronic document {DocumentId} has no number yet: {Code}", document.Id, exception.Code);

            return document;
        }
    }

    private async Task<ProcessOutcome> SendAsync(ElectronicDocumentAggregate document, IssuerAggregate issuer, IElectronicInvoiceProvider provider, Instant now, CancellationToken cancellationToken)
    {
        var range = provider.Capabilities.Numbering == NumberingMode.ByPlatform && document.NumberingRangeId is { } rangeId
            ? await numbering.DescribeAsync(rangeId, cancellationToken)
            : null;

        var providerIssuer = await resolver.ResolveAsync(issuer, cancellationToken);

        // A draft that already went through a round may have reached the provider without its answer coming back.
        if (document.Attempts > 0 && document.Number is not null && provider.Capabilities.Idempotency == IdempotencyMode.LookupByNumber)
        {
            var found = await provider.GetStateAsync(providerIssuer, LookupOf(document), cancellationToken);

            if (found is not null && found.Status != ProviderDocumentStatus.Rejected)
            {
                return await ApplyAsync(document, providerIssuer, provider, found, found.ProviderDocumentId, now, cancellationToken);
            }
        }

        var submission = await provider.SubmitAsync(providerIssuer, new ProviderDocument(document.Id, document.Type, document.Number, range, document.Content, IdempotencyKeys.For(document.Id)) { Revision = document.Revision }, cancellationToken);

        return await ApplyAsync(document, providerIssuer, provider, submission.State, submission.ProviderDocumentId ?? submission.State.ProviderDocumentId, now, cancellationToken);
    }

    private async Task<ProcessOutcome> CheckAsync(ElectronicDocumentAggregate document, IssuerAggregate issuer, IElectronicInvoiceProvider provider, Instant now, CancellationToken cancellationToken)
    {
        var providerIssuer = await resolver.ResolveAsync(issuer, cancellationToken);

        var state = await provider.GetStateAsync(providerIssuer, LookupOf(document), cancellationToken);

        if (state is null)
        {
            return await WaitAsync(document, now, "not-found-in-provider", cancellationToken);
        }

        return await ApplyAsync(document, providerIssuer, provider, state, state.ProviderDocumentId ?? document.ProviderDocumentId, now, cancellationToken);
    }

    private async Task<ProcessOutcome> ApplyAsync(ElectronicDocumentAggregate document, ProviderIssuer providerIssuer, IElectronicInvoiceProvider provider, ProviderDocumentState state, string? providerDocumentId, Instant now, CancellationToken cancellationToken)
    {
        var id = providerDocumentId ?? document.ProviderDocumentId;

        if (state.Status == ProviderDocumentStatus.Accepted && !string.IsNullOrWhiteSpace(state.Cufe) && !string.IsNullOrWhiteSpace(id))
        {
            var stored = await StoreFilesAsync(document, providerIssuer, provider, id, state.Number ?? document.Number, cancellationToken);

            var saved = await SaveAsync(document, x =>
            {
                x.Validate(id, state.Cufe, state.Number, state.IssueDate, state.Raw, stored, now);

                // Without its XML it is asked for again: the issuer must keep it.
                if (x.MissingFiles)
                {
                    RecordMissingFiles(x, stored, now);
                }

                return true;
            }, cancellationToken);

            return saved is null ? ProcessOutcome.Skipped : ProcessOutcome.Validated;
        }

        if (state.Status == ProviderDocumentStatus.Rejected)
        {
            var saved = await SaveAsync(document, x =>
            {
                x.Reject(state.RejectionOrigin ?? RejectionOrigin.Provider, state.Reasons, id, state.Raw, now);

                return true;
            }, cancellationToken);

            return saved is null ? ProcessOutcome.Skipped : ProcessOutcome.Rejected;
        }

        var contingency = state.Status == ProviderDocumentStatus.DianUnavailable;
        var stalled = false;

        var waiting = await SaveAsync(document, x =>
        {
            var next = now + options.Value.StatusCheck.DelayAfter(x.Attempts + 1);

            if (x.Status == ElectronicDocumentStatus.Draft && !string.IsNullOrWhiteSpace(id))
            {
                x.MarkSent(id, state.Raw, state.Number, contingency, now, next);
            }
            else if (x.Status == ElectronicDocumentStatus.Sent)
            {
                x.KeepWaiting(state.Raw, contingency, now, next);
            }
            else
            {
                x.RecordAttempt(now, "no-provider-id", next);
            }

            stalled = StallIfExhausted(x, now);

            return true;
        }, cancellationToken);

        if (waiting is null)
        {
            return ProcessOutcome.Skipped;
        }

        return stalled ? ProcessOutcome.Stalled : ProcessOutcome.Waiting;
    }

    private async Task<ProcessOutcome> KeepFilesAsync(ElectronicDocumentAggregate document, IssuerAggregate? issuer, IElectronicInvoiceProvider? provider, Instant now, CancellationToken cancellationToken)
    {
        DocumentFiles? stored = null;

        if (issuer is not null && provider is not null && !string.IsNullOrWhiteSpace(document.ProviderDocumentId))
        {
            try
            {
                var providerIssuer = await resolver.ResolveAsync(issuer, cancellationToken);

                stored = await StoreFilesAsync(document, providerIssuer, provider, document.ProviderDocumentId, document.Number, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("The credentials of issuer {IssuerId} could not be opened to keep the files of electronic document {DocumentId}: {Error}", issuer.Id, document.Id, exception.GetType().Name);
            }
        }

        var stalled = false;

        var saved = await SaveAsync(document, x =>
        {
            if (stored?.Xml is not null)
            {
                x.KeepFiles(stored, now);
            }
            else
            {
                stalled = RecordMissingFiles(x, stored, now);
            }

            return true;
        }, cancellationToken);

        if (saved is null)
        {
            return ProcessOutcome.Skipped;
        }

        if (stored?.Xml is not null)
        {
            return ProcessOutcome.FilesKept;
        }

        return stalled ? ProcessOutcome.Stalled : ProcessOutcome.Waiting;
    }

    private bool RecordMissingFiles(ElectronicDocumentAggregate document, DocumentFiles? partial, Instant now)
    {
        var check = options.Value.StatusCheck;
        var stalled = document.RecordMissingFiles(partial, now, now + check.DelayAfter(document.FilesAttempts + 1), check.MaxAttempts);

        if (stalled)
        {
            logger.LogError("The files of validated electronic document {DocumentId} could not be kept after {Attempts} attempts; it waits for someone to retry it.", document.Id, document.FilesAttempts);
        }

        return stalled;
    }

    private async Task<DocumentFiles?> StoreFilesAsync(ElectronicDocumentAggregate document, ProviderIssuer providerIssuer, IElectronicInvoiceProvider provider, string providerDocumentId, DocumentNumber? number, CancellationToken cancellationToken)
    {
        try
        {
            var handed = await provider.GetFilesAsync(providerIssuer, new ProviderDocumentLookup(document.Type, providerDocumentId, number, IdempotencyKeys.For(document.Id)), cancellationToken);

            return await files.StoreAsync(document.Tenant, document.Id, handed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The document is valid before the tax authority with or without its files here: it is validated anyway, and
            // its files are asked for again in the next rounds.
            logger.LogWarning("The files of electronic document {DocumentId} could not be stored: {Error}", document.Id, exception.GetType().Name);

            return null;
        }
    }

    private async Task<ProcessOutcome> WaitAsync(ElectronicDocumentAggregate document, Instant now, string outcome, CancellationToken cancellationToken)
    {
        var stalled = false;

        var saved = await SaveAsync(document, x =>
        {
            x.RecordAttempt(now, outcome, now + options.Value.StatusCheck.DelayAfter(x.Attempts + 1));

            stalled = StallIfExhausted(x, now);

            return true;
        }, cancellationToken);

        if (saved is null)
        {
            return ProcessOutcome.Skipped;
        }

        return stalled ? ProcessOutcome.Stalled : ProcessOutcome.Waiting;
    }

    private bool StallIfExhausted(ElectronicDocumentAggregate document, Instant now)
    {
        var check = options.Value.StatusCheck;
        var tooLong = document.FirstSentAt is { } firstSentAt && now - firstSentAt >= Duration.FromTimeSpan(check.SentTimeout);

        return (document.Attempts >= check.MaxAttempts || tooLong) && document.Stall(now);
    }

    private async Task<ElectronicDocumentAggregate?> ClaimAsync(Guid id, Instant now, CancellationToken cancellationToken)
    {
        var snapshot = await repository.FindAsync<ElectronicDocumentAggregate>(id, cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        var lease = now + Duration.FromTimeSpan(options.Value.StatusCheck.Lease);

        return await SaveAsync(snapshot, x => x.Claim(now, lease), cancellationToken);
    }

    /// <summary>
    /// Saves a change on the document only if nobody wrote it since it was read, and publishes its events.
    /// </summary>
    private async Task<ElectronicDocumentAggregate?> SaveAsync(ElectronicDocumentAggregate read, Func<ElectronicDocumentAggregate, bool> change, CancellationToken cancellationToken)
    {
        var version = read.Version;
        ElectronicDocumentAggregate? saved = null;

        await repository.UpdateWithRetryAsync(
            () => repository.FindAsync<ElectronicDocumentAggregate>(read.Id, cancellationToken),
            fresh =>
            {
                saved = null;

                if (fresh.Version != version || !change(fresh))
                {
                    return false;
                }

                saved = fresh;

                return true;
            },
            cancellationToken);

        if (saved is not null)
        {
            var events = saved.GetAndClearEvents();

            if (events.Count > 0)
            {
                await pubsub.PublishAsync(events, cancellationToken);
            }
        }

        return saved;
    }

    private async Task RecordCredentialRejectionAsync(IssuerAggregate issuer, string reasonCode, CancellationToken cancellationToken)
    {
        IssuerAggregate? saved = null;
        var settings = options.Value;

        await issuers.UpdateWithRetryAsync(
            () => issuers.FindAsync<IssuerAggregate>(issuer.Id, cancellationToken),
            fresh =>
            {
                fresh.GetAndClearEvents();
                fresh.RecordCredentialRejection(settings.SuspendAfterCredentialRejections, reasonCode, settings.SystemUser);

                saved = fresh;

                return true;
            },
            cancellationToken);

        var events = saved?.GetAndClearEvents() ?? [];

        if (events.Count > 0)
        {
            await pubsub.PublishAsync(events, cancellationToken);
        }
    }

    private async Task ForgetCredentialRejectionsAsync(IssuerAggregate issuer, ProcessOutcome outcome, CancellationToken cancellationToken)
    {
        if (issuer.ConsecutiveCredentialRejections == 0 || outcome == ProcessOutcome.Skipped)
        {
            return;
        }

        await issuers.UpdateWithRetryAsync(
            () => issuers.FindAsync<IssuerAggregate>(issuer.Id, cancellationToken),
            fresh => fresh.ResetCredentialRejections(),
            cancellationToken);
    }

    private static ProviderDocumentLookup LookupOf(ElectronicDocumentAggregate document) => new(document.Type, document.ProviderDocumentId, document.Number, IdempotencyKeys.For(document.Id));
}
