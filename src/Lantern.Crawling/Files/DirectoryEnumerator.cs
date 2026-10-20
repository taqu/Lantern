using Lantern.Core.Search;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Lantern.Crawling.Files;

public sealed class DirectoryEnumerator(ILogger<DirectoryEnumerator> logger)
{
    public async IAsyncEnumerable<DiscoveredFile> EnumerateAsync(
        string canonicalRoot,
        FileFilter filter,
        EnumerationState state,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stack = new Stack<string>();
        stack.Push(canonicalRoot);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = stack.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning("Access denied enumerating {Directory}: {Message}", dir, ex.Message);
                state.DirectoryErrors.Add($"Access denied: {dir}");
                continue;
            }
            catch (IOException ex)
            {
                logger.LogWarning("IO error enumerating {Directory}: {Message}", dir, ex.Message);
                state.DirectoryErrors.Add($"IO error: {dir}");
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try { info = new FileInfo(file); }
                catch (Exception ex)
                {
                    logger.LogDebug("Skipping inaccessible file {File}: {Message}", file, ex.Message);
                    continue;
                }
                if (!filter.ShouldIncludeFile(info)) continue;
                string canonical;
                try { canonical = DocumentIdentity.FilePath(file); }
                catch { continue; }
                yield return new DiscoveredFile(canonical, info.Length, info.LastWriteTimeUtc);
            }

            if (!filter.Recursive) continue;

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning("Access denied enumerating subdirectories of {Directory}: {Message}", dir, ex.Message);
                state.DirectoryErrors.Add($"Access denied (subdirs): {dir}");
                continue;
            }
            catch (IOException ex)
            {
                logger.LogWarning("IO error enumerating subdirectories of {Directory}: {Message}", dir, ex.Message);
                state.DirectoryErrors.Add($"IO error (subdirs): {dir}");
                continue;
            }

            foreach (string subdir in subdirs)
            {
                DirectoryInfo dirInfo;
                try { dirInfo = new DirectoryInfo(subdir); }
                catch { continue; }

                if (filter.ShouldExcludeDirectory(dirInfo)) continue;

                string canonicalSub;
                try { canonicalSub = DocumentIdentity.FilePath(subdir); }
                catch { continue; }

                // Boundary check: must be a descendant of root
                if (!IsDescendantOf(canonicalRoot, canonicalSub)) continue;

                stack.Push(subdir);
            }
        }

        state.Completed = true;
    }

    private static bool IsDescendantOf(string root, string path) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
        (path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar);
}
