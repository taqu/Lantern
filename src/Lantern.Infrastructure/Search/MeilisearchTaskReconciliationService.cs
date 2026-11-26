using Lantern.Core.Crawling;
using Meilisearch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Background service that polls Meilisearch for the outcome of submitted document
/// upsert tasks and transitions <see cref="CrawlSyncStatus.IndexingPending"/> records
/// to <see cref="CrawlSyncStatus.Indexed"/> or <see cref="CrawlSyncStatus.Failed"/>.
/// </summary>
public sealed class MeilisearchTaskReconciliationService : BackgroundService
{
    private readonly ICrawlStateRepository _stateRepository;
    private readonly IMeilisearchClientFactory _clientFactory;
    private readonly IMeilisearchCredentialManager _credentialManager;
    private readonly MeilisearchOptions _options;
    private readonly ILogger<MeilisearchTaskReconciliationService> _logger;

    private const int ReconciliationIntervalSeconds = 30;
    private const int BatchSize = 50;

    public MeilisearchTaskReconciliationService(
        ICrawlStateRepository stateRepository,
        IMeilisearchClientFactory clientFactory,
        IMeilisearchCredentialManager credentialManager,
        IOptions<MeilisearchOptions> options,
        ILogger<MeilisearchTaskReconciliationService> logger)
    {
        _stateRepository = stateRepository;
        _clientFactory = clientFactory;
        _credentialManager = credentialManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Meilisearch credentials before attempting any task queries
        while(!_credentialManager.IsReady && !stoppingToken.IsCancellationRequested)
            await Task.Delay(500, stoppingToken);

        if(stoppingToken.IsCancellationRequested) return;

        MeilisearchClient client = _clientFactory.GetIndexingClient();

        // Run an immediate reconciliation pass on startup to handle tasks that were pending
        // before the previous shutdown, then continue at the regular interval.
        await RunReconciliationPassAsync(client, stoppingToken);

        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(ReconciliationIntervalSeconds));
        try {
            while(await timer.WaitForNextTickAsync(stoppingToken)) {
                await RunReconciliationPassAsync(client, stoppingToken);
            }
        } catch(OperationCanceledException) {
        }
    }

    private async Task RunReconciliationPassAsync(MeilisearchClient client, CancellationToken ct)
    {
        try {
            IReadOnlyList<FileState> pending = await _stateRepository.FindPendingIndexingAsync(BatchSize, ct);
            if(pending.Count == 0) return;

            _logger.LogDebug("Reconciling {Count} pending Meilisearch indexing tasks", pending.Count);

            int resolved = 0;
            int failed = 0;

            foreach(FileState file in pending) {
                if(file.PendingMeilisearchTaskUid == null) continue;
                ct.ThrowIfCancellationRequested();

                int uid = file.PendingMeilisearchTaskUid.Value;
                try {
                    TaskResource task = await client.GetTaskAsync(uid, ct);

                    if(task.Status == TaskInfoStatus.Succeeded) {
                        file.SyncStatus = CrawlSyncStatus.Indexed;
                        file.PendingMeilisearchTaskUid = null;
                        file.LastError = null;
                        await _stateRepository.SaveAsync(file, ct);
                        resolved++;
                        _logger.LogInformation(
                            "Meilisearch task {TaskUid} succeeded for document {DocumentId} ({Path})",
                            uid, file.DocumentId, file.CanonicalPath);
                    } else if(task.Status is TaskInfoStatus.Failed or TaskInfoStatus.Canceled) {
                        string errorMsg = task.Error != null
                            ? System.Text.Json.JsonSerializer.Serialize(task.Error)
                            : $"Task {uid} ended with status {task.Status}";
                        file.SyncStatus = CrawlSyncStatus.Failed;
                        file.PendingMeilisearchTaskUid = null;
                        file.LastError = errorMsg;
                        await _stateRepository.SaveAsync(file, ct);
                        failed++;
                        _logger.LogWarning(
                            "Meilisearch task {TaskUid} failed for document {DocumentId} ({Path}): {Error}",
                            uid, file.DocumentId, file.CanonicalPath, errorMsg);
                    }
                    // Enqueued or Processing — leave as IndexingPending for the next pass
                } catch(OperationCanceledException) {
                    throw;
                } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
                    _logger.LogError("Meilisearch authentication failed during task reconciliation; stopping pass");
                    return;
                } catch(Exception ex) {
                    _logger.LogWarning(ex, "Could not check status of task {TaskUid} for document {DocumentId}; will retry next pass",
                        uid, file.DocumentId);
                }
            }

            if(resolved + failed > 0)
                _logger.LogInformation(
                    "Task reconciliation pass: {Resolved} indexed, {Failed} failed out of {Total} pending",
                    resolved, failed, pending.Count);
        } catch(OperationCanceledException) {
            throw;
        } catch(Exception ex) {
            _logger.LogError(ex, "Task reconciliation pass encountered an unexpected error");
        }
    }
}
