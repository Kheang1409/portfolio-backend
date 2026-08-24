using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using KaiAssistant.Infrastructure.Observability;
using System.Diagnostics.Metrics;

namespace KaiAssistant.Infrastructure.HostedServices;
public sealed class DocumentIngestionWorker(IServiceScopeFactory scopes, IOptionsMonitor<RagIngestionOptions> options, IInstanceIdentity identity, ILogger<DocumentIngestionWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("KaiAssistant.Ingestion", "1.0.0");
    private static readonly UpDownCounter<long> ActiveJobs = Meter.CreateUpDownCounter<long>("ingestion_worker_active_jobs");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = new HashSet<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            var limit = Math.Max(1, options.CurrentValue.MaxConcurrency);
            while (active.Count < limit && !stoppingToken.IsCancellationRequested)
            {
                IngestionJob? job;
                try { using var scope = scopes.CreateScope(); var config = options.CurrentValue; job = await scope.ServiceProvider.GetRequiredService<IIngestionJobStore>().ClaimAsync(identity.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(config.LeaseDurationSeconds), stoppingToken).ConfigureAwait(false); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Ingestion job claim failed"); break; }
                if (job is null) break;
                active.Add(ProcessJobAsync(job, stoppingToken));
            }
            if (active.Count == 0) { await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.PollingIntervalSeconds), stoppingToken).ConfigureAwait(false); continue; }
            var completed = await Task.WhenAny(active).ConfigureAwait(false); active.Remove(completed);
            try { await completed.ConfigureAwait(false); } catch (Exception ex) { logger.LogError(ex, "Ingestion task failed outside job boundary"); }
        }
        await Task.WhenAll(active).ConfigureAwait(false);
    }
    private async Task ProcessJobAsync(IngestionJob job, CancellationToken ct)
    {
        ActiveJobs.Add(1);
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider; var config = options.CurrentValue; var jobs = sp.GetRequiredService<IIngestionJobStore>();
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renewal = RenewLeaseAsync(jobs, job, config, leaseCts.Token);
        try { await ProcessAsync(sp, jobs, job, config, ct).ConfigureAwait(false); }
        finally { leaseCts.Cancel(); try { await renewal.ConfigureAwait(false); } catch (OperationCanceledException) { } ActiveJobs.Add(-1); }
    }
    private async Task ProcessAsync(IServiceProvider sp, IIngestionJobStore jobs, IngestionJob job, RagIngestionOptions config, CancellationToken ct)
    {
        try
        {
            var documents = sp.GetRequiredService<IKnowledgeDocumentStore>(); var document = await documents.GetAsync(job.DocumentId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Document not found");
            await documents.SetStatusAsync(document.Id!, "Processing", cancellationToken: ct).ConfigureAwait(false);
            var parser = sp.GetServices<IDocumentParser>().FirstOrDefault(x => x.CanParse(document.MimeType)) ?? throw new InvalidOperationException("Unsupported document type");
            await using var stream = await sp.GetRequiredService<IDocumentStorage>().OpenReadAsync(document.StorageKey, ct).ConfigureAwait(false);
            var text = await parser.ExtractTextAsync(stream, ct).ConfigureAwait(false);
            var count = await sp.GetRequiredService<IDocumentIngestionService>().IndexExistingAsync(document.Id!, document.Source, document.Title, document.MimeType, text, document.Metadata, ct).ConfigureAwait(false);
            await documents.SetStatusAsync(document.Id!, "Indexed", count, ct).ConfigureAwait(false); await jobs.CompleteAsync(job.Id!, identity.InstanceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var dead = job.AttemptCount >= job.MaxAttempts; var delay = TimeSpan.FromSeconds(Math.Min(300, config.RetryBaseDelaySeconds * Math.Pow(2, job.AttemptCount - 1)) + Random.Shared.NextDouble());
            await jobs.FailAsync(job.Id!, identity.InstanceId, "INGESTION_FAILED", ex.Message, DateTimeOffset.UtcNow.Add(delay), dead, ct).ConfigureAwait(false);
            logger.LogWarning(ex, "Ingestion job failed: {JobId} document={DocumentId}", job.Id, job.DocumentId);
        }
    }
    private async Task RenewLeaseAsync(IIngestionJobStore jobs, IngestionJob job, RagIngestionOptions config, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, config.LeaseDurationSeconds / 2));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct).ConfigureAwait(false);
            if (!await jobs.RenewLeaseAsync(job.Id!, identity.InstanceId, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(config.LeaseDurationSeconds), ct).ConfigureAwait(false)) throw new InvalidOperationException("Ingestion job lease was lost.");
        }
    }
}
