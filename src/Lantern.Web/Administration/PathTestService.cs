using Lantern.Core.Search;

namespace Lantern.Web.Administration;

public sealed record PathTestResult(bool Success, string Message);

// Tests server-side filesystem accessibility. Does not modify files, index documents, or update crawl state.
// Uses the Lantern process identity — browser credentials do not grant server access to UNC shares.
public sealed class PathTestService
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public async Task<PathTestResult> TestAsync(string rootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return new PathTestResult(false, "Root path is required.");

        string canonical;
        try { canonical = DocumentIdentity.FilePath(rootPath); }
        catch (ArgumentException ex) { return new PathTestResult(false, ex.Message); }
        catch { return new PathTestResult(false, "Path format is not valid."); }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TestTimeout);

        try
        {
            bool exists = await Task.Run(() => Directory.Exists(canonical), timeoutCts.Token);
            if (!exists)
                return new PathTestResult(false, "Directory does not exist or cannot be reached.");

            await Task.Run(() =>
            {
                // Bounded non-recursive enumeration to verify read access without full scan.
                _ = Directory.EnumerateFileSystemEntries(canonical).Take(10).ToList();
            }, timeoutCts.Token);

            return new PathTestResult(true, "Directory is accessible.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PathTestResult(false, "Path test timed out.");
        }
        catch (UnauthorizedAccessException)
        {
            return new PathTestResult(false, "Access denied. The server process does not have permission to read this directory.");
        }
        catch (DirectoryNotFoundException)
        {
            return new PathTestResult(false, "Directory not found.");
        }
        catch (IOException)
        {
            return new PathTestResult(false, "I/O error. The network path may be unavailable.");
        }
        catch (Exception ex)
        {
            return new PathTestResult(false, $"Test failed: {ex.Message}");
        }
    }
}
