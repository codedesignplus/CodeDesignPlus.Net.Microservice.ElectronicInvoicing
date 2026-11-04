using CodeDesignPlus.Net.Hangfire.Abstractions;
using CodeDesignPlus.Net.Hangfire.Abstractions.Attributes;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessDueElectronicDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.NumberingRange;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using Hangfire;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Jobs;

/// <summary>
/// Sends the drafts and asks about the sent documents whose next look is due: the safety net of the consumer of the
/// queued event, and the only way the results arrive while the provider's updates are asked for.
/// </summary>
/// <remarks>
/// Every five minutes: each document carries its own backoff (<c>ElectronicInvoicing:StatusCheck</c>), so running more
/// often only looks at the ones that are due. What it does is signed by the system user of the configuration.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[RecurringJobOptions("*/5 * * * *", jobId: "electronic-document-status-job")]
public class ElectronicDocumentStatusJob(IMediator mediator, ILogger<ElectronicDocumentStatusJob> logger) : IRecurrentJob
{
    /// <inheritdoc/>
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        logger.LogInformation("ElectronicDocumentStatusJob: looking at the electronic documents that are due.");

        await mediator.Send(new ProcessDueElectronicDocumentsCommand(), cancellationToken.ShutdownToken);
    }
}

/// <summary>
/// Announces the numbering ranges that run out of numbers or of validity, so the issuer asks for a new resolution in
/// time.
/// </summary>
/// <remarks>
/// Once a day, at 11:00 UTC (six in the morning in Colombia). Each range is announced once per alert.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[RecurringJobOptions("0 11 * * *", jobId: "numbering-range-watch-job")]
public class NumberingRangeWatchJob(IMediator mediator, ILogger<NumberingRangeWatchJob> logger) : IRecurrentJob
{
    /// <inheritdoc/>
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        var raised = await mediator.Send(new WatchNumberingRangesCommand(), cancellationToken.ShutdownToken);

        logger.LogInformation("NumberingRangeWatchJob: {Raised} alerts raised.", raised);
    }
}

/// <summary>
/// Counts the documents sent with the provider's plan and announces the thresholds crossed and the end of the contract.
/// </summary>
/// <remarks>
/// Every hour: the count is one query over the documents, and a threshold is announced once.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[RecurringJobOptions("15 * * * *", jobId: "provider-plan-watch-job")]
public class ProviderPlanWatchJob(IMediator mediator, ILogger<ProviderPlanWatchJob> logger) : IRecurrentJob
{
    /// <inheritdoc/>
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        var used = await mediator.Send(new WatchProviderPlanCommand(), cancellationToken.ShutdownToken);

        logger.LogInformation("ProviderPlanWatchJob: {Used} documents sent in the period of the plan.", used);
    }
}
