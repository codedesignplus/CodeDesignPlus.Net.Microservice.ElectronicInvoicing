namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;

/// <summary>
/// One round of an electronic document: sends it if it is waiting, asks for its answer if it was sent.
/// </summary>
/// <remarks>
/// The consumer of the queued event, the status job, a retry of the caller and a call of the provider run it; all of
/// them on the same document is safe, because each round claims the document first.
/// </remarks>
/// <param name="Id">The id of the document.</param>
public record ProcessElectronicDocumentCommand(Guid Id) : IRequest<ProcessOutcome>;

/// <summary>
/// What a round did with a document.
/// </summary>
public enum ProcessOutcome
{
    /// <summary>
    /// Nothing: it was final, stalled, missing or another process had it.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// It is still waiting (to be sent, for the document before it, or for an answer); it is looked at again later.
    /// </summary>
    Waiting = 2,

    /// <summary>
    /// The tax authority validated it.
    /// </summary>
    Validated = 3,

    /// <summary>
    /// It was rejected.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// It ran out of retries without a final answer.
    /// </summary>
    Stalled = 5,

    /// <summary>
    /// The provider or the Vault did not answer; it is retried later.
    /// </summary>
    Deferred = 6
}

/// <summary>
/// Validates <see cref="ProcessElectronicDocumentCommand"/>.
/// </summary>
public class Validator : AbstractValidator<ProcessElectronicDocumentCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
