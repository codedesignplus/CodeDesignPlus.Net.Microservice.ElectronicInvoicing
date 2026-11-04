namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.CreateElectronicDocument;

/// <summary>
/// Handles <see cref="CreateElectronicDocumentCommand"/>.
/// </summary>
/// <param name="repository">The repository of the documents.</param>
/// <param name="user">The context of the user who asks.</param>
/// <param name="pubsub">The bus where the events are published.</param>
public class CreateElectronicDocumentCommandHandler(IElectronicDocumentRepository repository, IUserContext user, IPubSub pubsub) : IRequestHandler<CreateElectronicDocumentCommand>
{
    /// <summary>
    /// Creates the document unless a previous request with the same id already did.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(CreateElectronicDocumentCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        // The same id is the retry of the same request: it ends well without issuing again.
        if (await repository.ExistsAsync<ElectronicDocumentAggregate>(request.Id, cancellationToken))
        {
            return;
        }

        var document = ElectronicDocumentAggregate.Create(request.Id, user.Tenant, request.Reference, user.IdUser);

        // Two requests that arrive at once both pass the check above; the second one hits the id and publishes nothing.
        if (!await repository.CreateIfAbsentAsync(document, cancellationToken))
        {
            return;
        }

        await pubsub.PublishAsync(document.GetAndClearEvents(), cancellationToken);
    }
}
