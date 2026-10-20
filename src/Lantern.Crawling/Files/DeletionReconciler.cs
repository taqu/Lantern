using Lantern.Core.Crawling;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Files;

public sealed class DeletionReconciler(
    ICrawlStateRepository stateRepository,
    IDocumentIndexingService documentIndexing,
    IChunkIndexingService chunkIndexing,
    ILogger<DeletionReconciler> logger)
{
    public async Task<(int Deleted, List<string> Errors)> ReconcileAsync(
        Guid crawlerId,
        Guid scanId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FileState> candidates = await stateRepository.GetNotSeenInScanAsync(crawlerId, scanId, cancellationToken);
        int deleted = 0;
        var errors = new List<string>();

        foreach (FileState candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await chunkIndexing.DeleteChunksByDocumentAsync(candidate.DocumentId, cancellationToken);
                await documentIndexing.DeleteAsync(candidate.DocumentId, cancellationToken);
                await stateRepository.DeleteAsync(candidate.Id, cancellationToken);
                deleted++;
                logger.LogInformation("Reconciled deleted document {DocumentId} ({Path})", candidate.DocumentId, candidate.CanonicalPath);
            }
            catch (Exception ex)
            {
                string msg = $"Failed to delete {candidate.CanonicalPath}: {ex.Message}";
                errors.Add(msg);
                logger.LogError(ex, "Reconciliation failed for {DocumentId} ({Path})", candidate.DocumentId, candidate.CanonicalPath);
            }
        }

        return (deleted, errors);
    }
}
