using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Options;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using Microsoft.Extensions.Options;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessProviderEvent;

/// <summary>
/// A provider called with the result of a document.
/// </summary>
/// <param name="Provider">The code of the provider in the URL.</param>
/// <param name="Body">The body, as it came.</param>
/// <param name="Headers">The headers.</param>
public record ProcessProviderEventCommand(string Provider, string Body, IReadOnlyDictionary<string, string> Headers) : IRequest<bool>;

/// <summary>
/// Handles <see cref="ProcessProviderEventCommand"/>.
/// </summary>
/// <remarks>
/// The call is only a hint: when it is authentic, the document it names is looked at now, and its state is asked to the
/// provider like in any other round. What the call says is never applied as it comes. A provider without the capability,
/// or whose updates are configured to be asked for, is ignored; so is a call whose signature does not match.
/// </remarks>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="repository">The repository of the documents.</param>
/// <param name="mediator">Runs the round of the document.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The options of the service.</param>
/// <param name="logger">The logger.</param>
public class ProcessProviderEventCommandHandler(IElectronicInvoiceProviderFactory factory, IElectronicDocumentRepository repository, IMediator mediator, IClock clock, IOptions<ElectronicInvoicingOptions> options, ILogger<ProcessProviderEventCommandHandler> logger) : IRequestHandler<ProcessProviderEventCommand, bool>
{
    /// <summary>
    /// Brings forward the round of the document the call names.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the call was trusted and named a known document.</returns>
    public async Task<bool> Handle(ProcessProviderEventCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var reader = factory.FindEvents(request.Provider ?? string.Empty);

        if (reader is null || reader.Capabilities.StatusUpdates != StatusUpdatesMode.Push)
        {
            logger.LogInformation("A call from provider {Provider} was ignored: its updates are not received by calls.", request.Provider);

            return false;
        }

        if (!reader.IsAuthentic(request.Body ?? string.Empty, request.Headers ?? new Dictionary<string, string>()))
        {
            logger.LogWarning("A call from provider {Provider} was discarded: its signature does not match.", request.Provider);

            return false;
        }

        var providerEvent = await reader.ReadAsync(request.Body!, request.Headers!, cancellationToken);
        var document = providerEvent is null ? null : await repository.FindByProviderDocumentIdAsync(providerEvent.ProviderDocumentId, cancellationToken);

        if (document is null)
        {
            logger.LogInformation("A call from provider {Provider} names no known document.", request.Provider);

            return false;
        }

        var now = clock.GetCurrentInstant();

        await repository.UpdateWithRetryAsync(
            () => repository.FindAsync<ElectronicDocumentAggregate>(document.Id, cancellationToken),
            fresh => fresh.Expedite(now, options.Value.SystemUser),
            cancellationToken);

        await mediator.Send(new ProcessElectronicDocumentCommand(document.Id), cancellationToken);

        return true;
    }
}
