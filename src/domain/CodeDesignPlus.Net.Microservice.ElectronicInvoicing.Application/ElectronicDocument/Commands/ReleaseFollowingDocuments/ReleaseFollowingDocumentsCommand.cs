using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ReleaseFollowingDocuments;

/// <summary>
/// A document was validated: the drafts that waited for it are sent now, instead of waiting for their next look.
/// </summary>
/// <param name="PrecededBy">The id of the validated document.</param>
public record ReleaseFollowingDocumentsCommand(Guid PrecededBy) : IRequest<int>;

/// <summary>
/// Handles <see cref="ReleaseFollowingDocumentsCommand"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="mediator">Runs the round of each document.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
public class ReleaseFollowingDocumentsCommandHandler(IElectronicDocumentRepository repository, IMediator mediator, IClock clock, IOptions<ElectronicInvoicingOptions> options) : IRequestHandler<ReleaseFollowingDocumentsCommand, int>
{
    /// <summary>
    /// Brings forward and sends the drafts that waited.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many were released.</returns>
    public async Task<int> Handle(ReleaseFollowingDocumentsCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var followers = await repository.FindWaitingOnAsync(request.PrecededBy, cancellationToken);
        var now = clock.GetCurrentInstant();

        foreach (var follower in followers)
        {
            await repository.UpdateWithRetryAsync(
                () => repository.FindAsync<ElectronicDocumentAggregate>(follower.Id, cancellationToken),
                fresh => fresh.Expedite(now, options.Value.SystemUser),
                cancellationToken);

            await mediator.Send(new ProcessElectronicDocumentCommand(follower.Id), cancellationToken);
        }

        return followers.Count;
    }
}
