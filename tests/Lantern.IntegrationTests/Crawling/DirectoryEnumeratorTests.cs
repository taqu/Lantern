using Lantern.Core.Crawling;
using Lantern.Crawling.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests.Crawling;

public sealed class DirectoryEnumeratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lantern-enum-test-" + Guid.NewGuid().ToString("N"));

    public DirectoryEnumeratorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DirectoryEnumerator CreateEnumerator() =>
        new(NullLogger<DirectoryEnumerator>.Instance);

    private FileFilter CreateFilter(
        IReadOnlyList<string>? extensions = null,
        bool recursive = true,
        IReadOnlyList<string>? excluded = null) =>
        new(new FileCrawlerConfiguration
        {
            CrawlerId = Guid.NewGuid(),
            RootPath = _root,
            Recursive = recursive,
            IncludedExtensions = extensions ?? [],
            ExcludedDirectories = excluded ?? [],
            MaxFileSizeMB = 100
        });

    [Fact]
    public async Task EnumeratesFilesInRootDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "world");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.Equal(2, files.Count);
        Assert.True(state.Completed);
    }

    [Fact]
    public async Task RecursiveEnumerationFindsNestedFiles()
    {
        DirectoryInfo sub = Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sub.FullName, "nested.txt"), "nested");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(_root, CreateFilter(recursive: true), state).ToListAsync();

        Assert.Equal(2, files.Count);
        Assert.True(state.Completed);
    }

    [Fact]
    public async Task NonRecursiveEnumerationSkipsSubdirectories()
    {
        DirectoryInfo sub = Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sub.FullName, "nested.txt"), "nested");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(_root, CreateFilter(recursive: false), state).ToListAsync();

        Assert.Single(files);
        Assert.Contains(files, f => f.CanonicalPath.EndsWith("top.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtensionFilterExcludesNonMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_root, "doc.pdf"), "");
        File.WriteAllText(Path.Combine(_root, "data.xlsx"), "");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(_root, CreateFilter(extensions: [".pdf"]), state).ToListAsync();

        Assert.Single(files);
        Assert.Contains(files, f => f.CanonicalPath.EndsWith("doc.pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExcludedDirectoriesAreSkipped()
    {
        DirectoryInfo excluded = Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        File.WriteAllText(Path.Combine(_root, "main.txt"), "");
        File.WriteAllText(Path.Combine(excluded.FullName, "ignored.txt"), "");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(
            _root, CreateFilter(excluded: ["node_modules"]), state).ToListAsync();

        Assert.Single(files);
        Assert.DoesNotContain(files, f => f.CanonicalPath.Contains("node_modules"));
    }

    [Fact]
    public async Task CancellationStopsEnumeration()
    {
        for (int i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"file{i}.txt"), "content");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        var files = new List<DiscoveredFile>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (DiscoveredFile f in enumerator.EnumerateAsync(_root, CreateFilter(), state, cts.Token))
                files.Add(f);
        });
    }

    [Fact]
    public async Task CompletedFlagSetAfterFullEnumeration()
    {
        File.WriteAllText(Path.Combine(_root, "file.txt"), "x");

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        await enumerator.EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        Assert.True(state.Completed);
        Assert.Empty(state.DirectoryErrors);
    }

    [Fact]
    public async Task FileFingerprintContainsCorrectMetadata()
    {
        string content = "hello world";
        string path = Path.Combine(_root, "test.txt");
        File.WriteAllText(path, content);
        var info = new FileInfo(path);

        DirectoryEnumerator enumerator = CreateEnumerator();
        var state = new EnumerationState();
        List<DiscoveredFile> files = await enumerator.EnumerateAsync(_root, CreateFilter(), state).ToListAsync();

        DiscoveredFile file = Assert.Single(files);
        Assert.Equal(info.Length, file.FileSize);
        Assert.Equal(info.LastWriteTimeUtc, file.LastWriteTimeUtc);
        Assert.True(file.CanonicalPath.EndsWith("test.txt", StringComparison.OrdinalIgnoreCase));
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (T? item in source) list.Add(item);
        return list;
    }
}
