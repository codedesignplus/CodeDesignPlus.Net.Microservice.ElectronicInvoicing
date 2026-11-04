using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ReleaseFollowingDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Consumers;

/// <summary>
/// Sends a document as soon as it is queued (created, corrected or brought forward), instead of waiting for the status
/// job.
/// </summary>
/// <remarks>
/// The round claims the document first, so a redelivery or the job looking at it at the same time do not send it twice.
/// A document that waits for another one stays waiting until that one is validated.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[QueueName<ElectronicDocumentAggregate>("ElectronicDocumentQueuedHandler")]
public class ElectronicDocumentQueuedHandler(IMediator mediator, ILogger<ElectronicDocumentQueuedHandler> logger) : IEventHandler<ElectronicDocumentQueuedDomainEvent>
{
    /// <summary>
    /// Runs the round of the document.
    /// </summary>
    /// <param name="data">The event.</param>
    /// <param name="token">The cancellation token.</param>
    public async Task HandleAsync(ElectronicDocumentQueuedDomainEvent data, CancellationToken token)
    {
        var outcome = await mediator.Send(new ProcessElectronicDocumentCommand(data.AggregateId), token);

        logger.LogInformation("Electronic document {DocumentId} queued: {Outcome}.", data.AggregateId, outcome);
    }
}

/// <summary>
/// When a document is validated, the drafts that waited for it are sent (a credit note before the invoice it makes room
/// for).
/// </summary>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[QueueName<ElectronicDocumentAggregate>("ElectronicDocumentValidatedHandler")]
public class ElectronicDocumentValidatedHandler(IMediator mediator, ILogger<ElectronicDocumentValidatedHandler> logger) : IEventHandler<ElectronicDocumentValidatedDomainEvent>
{
    /// <summary>
    /// Releases the drafts that waited.
    /// </summary>
    /// <param name="data">The event.</param>
    /// <param name="token">The cancellation token.</param>
    public async Task HandleAsync(ElectronicDocumentValidatedDomainEvent data, CancellationToken token)
    {
        var released = await mediator.Send(new ReleaseFollowingDocumentsCommand(data.AggregateId), token);

        if (released > 0)
        {
            logger.LogInformation("Electronic document {DocumentId} validated: {Released} documents that waited for it were sent.", data.AggregateId, released);
        }
    }
}
