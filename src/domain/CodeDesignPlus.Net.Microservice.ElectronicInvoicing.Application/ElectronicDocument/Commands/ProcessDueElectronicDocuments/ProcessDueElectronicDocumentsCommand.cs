using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessDueElectronicDocuments;

/// <summary>
/// The round of the status job: sends the drafts and asks about the sent documents whose next look is due.
/// </summary>
public record ProcessDueElectronicDocumentsCommand : IRequest<DueDocumentsSummary>;

/// <summary>
/// What a round of the status job did.
/// </summary>
/// <param name="Looked">Documents looked at.</param>
/// <param name="ByOutcome">How many ended each way.</param>
/// <param name="Failed">Documents whose round failed unexpectedly.</param>
public sealed record DueDocumentsSummary(int Looked, IReadOnlyDictionary<ProcessOutcome, int> ByOutcome, int Failed);

/// <summary>
/// Handles <see cref="ProcessDueElectronicDocumentsCommand"/>.
/// </summary>
/// <remarks>
/// Every due document is tried, whatever happens to the others, and each one keeps the outcome of its round (rule 23).
/// A provider that does not answer is not a failure: the document waits its backoff. An unexpected failure is counted
/// and, at the end, the round throws so the dashboard does not show it in green.
/// </remarks>
/// <param name="repository">The repository of the documents.</param>
/// <param name="mediator">Runs the round of each document.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
/// <param name="logger">The logger.</param>
public class ProcessDueElectronicDocumentsCommandHandler(IElectronicDocumentRepository repository, IMediator mediator, IClock clock, IOptions<ElectronicInvoicingOptions> options, ILogger<ProcessDueElectronicDocumentsCommandHandler> logger) : IRequestHandler<ProcessDueElectronicDocumentsCommand, DueDocumentsSummary>
{
    /// <summary>
    /// Runs the round.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the round did.</returns>
    /// <exception cref="InvalidOperationException">Thrown at the end when the round of any document failed.</exception>
    public async Task<DueDocumentsSummary> Handle(ProcessDueElectronicDocumentsCommand request, CancellationToken cancellationToken)
    {
        var due = await repository.FindDueAsync(clock.GetCurrentInstant(), options.Value.StatusCheck.BatchSize, cancellationToken);
        var byOutcome = new Dictionary<ProcessOutcome, int>();
        var failed = 0;

        foreach (var document in due)
        {
            try
            {
                var outcome = await mediator.Send(new ProcessElectronicDocumentCommand(document.Id), cancellationToken);

                byOutcome[outcome] = byOutcome.GetValueOrDefault(outcome) + 1;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;

                logger.LogError(exception, "The round of electronic document {DocumentId} failed.", document.Id);
            }
        }

        logger.LogInformation("Electronic documents round: {Looked} looked at, {Failed} failed, {Outcomes}.", due.Count, failed, string.Join(", ", byOutcome.Select(x => $"{x.Key} {x.Value}")));

        // Without this, a round where every document failed would show in green on the dashboard.
        if (failed > 0)
        {
            throw new InvalidOperationException($"The round of {failed} of {due.Count} electronic documents failed.");
        }

        return new DueDocumentsSummary(due.Count, byOutcome, failed);
    }
}
