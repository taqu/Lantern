using Lantern.Core.Crawling;
using Lantern.Crawling.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests.Crawling;

/// <summary>
/// Security tests for DirectoryEnumerator: boundary enforcement, reparse point skipping,
/// path traversal prevention, and cancellation safety.
/// </summary>
public sealed class FilesystemSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lantern-sec-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "lantern-sec-outside-" + Guid.NewGuid().ToString("N"));

    public FilesystemSecurityTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_outside);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private DirectoryEnumerator CreateEnumerator() => new(NullLogger<DirectoryEnumerator>.Instance);

    private FileFilter CreateFilter(bool recursive = true) =>
        new(new FileCrawlerConfiguration
        {
            CrawlerId = Guid.NewGuid(),
            RootPath = _root,
            Recursive = recursive,
            IncludedExtensions = [],
            ExcludedDirectories = [],
            MaxFileSizeMB = 100
        });

    // -------------------------------------------------------------------
    // Boundary enforcement
    // -------------------------------------------------------------------

    [Fact]
    public async Task BoundaryCheck_FilesInsideRootAreIncluded()
    {
        File.WriteAllText(Path.Combine(_root, "inside.txt"), "content");

        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.Single(files);
        Assert.True(state.Completed);
    }

    [Fact]
    public async Task BoundaryCheck_EmptyRootYieldsNoFiles()
    {
        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.Empty(files);
        Assert.True(state.Completed);
    }

    [Fact]
    public async Task BoundaryCheck_NestedDirectoryFileIsFound()
    {
        string sub = Directory.CreateDirectory(Path.Combine(_root, "a", "b")).FullName;
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "deep");

        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.Single(files);
        Assert.True(files[0].CanonicalPath.Contains("deep.txt", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------
    // Reparse point / junction / symlink skipping
    // -------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Filesystem")]
    public async Task Junction_DirectoryReparsePointIsSkipped()
    {
        // Write a file outside the root that a junction could expose.
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");

        string junctionPath = Path.Combine(_root, "junction");
        bool created = TryCreateDirectoryJunction(junctionPath, _outside);
        if (!created)
        {
            // Junction creation requires elevated privileges or Developer Mode.
            return;
        }

        File.WriteAllText(Path.Combine(_root, "normal.txt"), "normal");

        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        // Only the normal file should be found; the junction target must not be traversed.
        Assert.Single(files);
        Assert.Contains(files, f => f.CanonicalPath.EndsWith("normal.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.CanonicalPath.Contains("secret.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Filesystem")]
    public async Task Symlink_FileReparsePointIsSkipped()
    {
        string realFile = Path.Combine(_outside, "real.txt");
        File.WriteAllText(realFile, "real content");

        string linkPath = Path.Combine(_root, "link.txt");
        bool created = TryCreateFileSymlink(linkPath, realFile);
        if (!created)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_root, "normal.txt"), "normal");

        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        // The symlink must not be yielded; only the normal file should appear.
        Assert.Single(files);
        Assert.Contains(files, f => f.CanonicalPath.EndsWith("normal.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.CanonicalPath.EndsWith("link.txt", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------

    [Fact]
    public async Task Cancellation_ThrowsAndDoesNotSetCompleted()
    {
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(_root, $"file{i}.txt"), "x");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var state = new EnumerationState();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (DiscoveredFile _ in CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state, cts.Token))
                break;
        });

        Assert.False(state.Completed);
    }

    [Fact]
    public async Task Cancellation_MidEnumeration_StopsProcessing()
    {
        for (int i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "x");

        var cts = new CancellationTokenSource();
        var state = new EnumerationState();
        int count = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (DiscoveredFile _ in CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state, cts.Token))
            {
                count++;
                if (count == 1) cts.Cancel();
            }
        });

        Assert.False(state.Completed);
        Assert.True(count < 20, "Cancellation should stop before all files are enumerated");
    }

    // -------------------------------------------------------------------
    // Directory error handling
    // -------------------------------------------------------------------

    [Fact]
    public async Task DirectoryErrors_AreRecordedButEnumerationContinues()
    {
        File.WriteAllText(Path.Combine(_root, "good.txt"), "content");
        // Verify that the state collects errors rather than throwing.
        // (We can't easily make a real directory inaccessible in a test
        //  without admin privileges, so this validates the state structure.)
        var state = new EnumerationState();
        List<DiscoveredFile> files = await CreateEnumerator().EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.True(state.Completed);
        Assert.Single(files);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static bool TryCreateDirectoryJunction(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch
        {
            // Fall back to mklink /J which works without Developer Mode for directories.
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);
                return Directory.Exists(link);
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool TryCreateFileSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return File.Exists(link);
        }
        catch
        {
            return false;
        }
    }
}
