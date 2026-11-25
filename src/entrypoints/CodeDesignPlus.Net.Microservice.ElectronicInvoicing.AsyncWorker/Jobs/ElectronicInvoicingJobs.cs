using CodeDesignPlus.Net.Hangfire.Abstractions;
using CodeDesignPlus.Net.Hangfire.Abstractions.Attributes;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessDueElectronicDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;
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
/// Counts the documents of the current month of the provider's plan, with its overage and its estimated cost, and
/// announces the thresholds crossed and the end of the contract. Going over the plan never blocks an issue.
/// </summary>
/// <remarks>
/// Every hour: the count is one query over the documents, and a threshold is announced once in the month. The first
/// round of a month also closes the previous one.
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

        logger.LogInformation("ProviderPlanWatchJob: {Used} documents of the provider's plan in the current month.", used);
    }
}

/// <summary>
/// Registers the webhooks of this service in the issuers already created in a provider that do not have them as the
/// configuration has them now: the ones created before the webhooks were turned on, or before their address or secret
/// changed.
/// </summary>
/// <remarks>
/// Every hour, at minute 45. With nothing to register it is one query; it can also be run at once from the panel of the
/// jobs after turning the webhooks on.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[RecurringJobOptions("45 * * * *", jobId: "provider-webhooks-sync-job")]
public class ProviderWebhooksSyncJob(IMediator mediator, ILogger<ProviderWebhooksSyncJob> logger) : IRecurrentJob
{
    /// <inheritdoc/>
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        var registered = await mediator.Send(new SyncProviderWebhooksCommand(), cancellationToken.ShutdownToken);

        logger.LogInformation("ProviderWebhooksSyncJob: webhooks registered for {Registered} issuers.", registered);
    }
}

/// <summary>
/// Aligns every issuer with the tax regime and the fiscal responsibilities in force today by the history managed from
/// outside it: the versions whose day came, and the issuers whose provider refused them or did not answer before.
/// </summary>
/// <remarks>
/// Every hour, at minute 20, so a version that rules from a day is applied in the first hour of that day in the time zone
/// of its tenant. With nothing to change it only reads; what happened to each issuer stays on it, and the round fails in
/// the panel when any issuer was not aligned.
/// </remarks>
/// <param name="mediator">Mediator that sends the command.</param>
/// <param name="logger">The logger.</param>
[RecurringJobOptions("20 * * * *", jobId: "managed-fiscal-profile-job")]
public class ManagedFiscalProfileJob(IMediator mediator, ILogger<ManagedFiscalProfileJob> logger) : IRecurrentJob
{
    /// <inheritdoc/>
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        var applied = await mediator.Send(new ApplyManagedFiscalProfilesCommand(), cancellationToken.ShutdownToken);

        logger.LogInformation("ManagedFiscalProfileJob: {Applied} issuers took new fiscal data.", applied);
    }
}
