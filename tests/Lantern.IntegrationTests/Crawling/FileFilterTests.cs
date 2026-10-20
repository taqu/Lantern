using Lantern.Core.Crawling;
using Lantern.Crawling.Files;

namespace Lantern.IntegrationTests.Crawling;

public sealed class FileFilterTests
{
    private static FileCrawlerConfiguration Config(
        IReadOnlyList<string>? extensions = null,
        IReadOnlyList<string>? excludedDirs = null,
        int maxFileSizeMB = 100) =>
        new()
        {
            CrawlerId = Guid.NewGuid(),
            RootPath = @"C:\test",
            IncludedExtensions = extensions ?? [".pdf", ".txt"],
            ExcludedDirectories = excludedDirs ?? ["node_modules", ".git"],
            MaxFileSizeMB = maxFileSizeMB
        };

    [Fact]
    public void IncludedExtensionAllowsMatchingFiles()
    {
        var filter = new FileFilter(Config(extensions: [".pdf", ".txt"]));
        FileInfo file = CreateFile("report.pdf", 1024);
        Assert.True(filter.ShouldIncludeFile(file));
    }

    [Fact]
    public void ExcludedExtensionRejectsFile()
    {
        var filter = new FileFilter(Config(extensions: [".pdf"]));
        FileInfo file = CreateFile("data.xlsx", 1024);
        Assert.False(filter.ShouldIncludeFile(file));
    }

    [Fact]
    public void EmptyInclusionListAllowsAllExtensions()
    {
        var filter = new FileFilter(Config(extensions: []));
        FileInfo file = CreateFile("anything.xyz", 1024);
        Assert.True(filter.ShouldIncludeFile(file));
    }

    [Fact]
    public void FileLargerThanLimitIsRejected()
    {
        var filter = new FileFilter(Config(maxFileSizeMB: 1));
        FileInfo file = CreateFile("huge.pdf", 2 * 1024 * 1024);
        Assert.False(filter.ShouldIncludeFile(file));
    }

    [Fact]
    public void FileAtExactLimitIsAccepted()
    {
        var filter = new FileFilter(Config(maxFileSizeMB: 1));
        FileInfo file = CreateFile("exact.pdf", 1 * 1024 * 1024);
        Assert.True(filter.ShouldIncludeFile(file));
    }

    [Fact]
    public void ExcludedDirectoryNameIsRejected()
    {
        var filter = new FileFilter(Config(excludedDirs: ["node_modules", ".git"]));
        DirectoryInfo dir = CreateDir("node_modules");
        Assert.True(filter.ShouldExcludeDirectory(dir));
    }

    [Fact]
    public void NonExcludedDirectoryIsAllowed()
    {
        var filter = new FileFilter(Config(excludedDirs: ["node_modules"]));
        DirectoryInfo dir = CreateDir("src");
        Assert.False(filter.ShouldExcludeDirectory(dir));
    }

    [Fact]
    public void ExtensionMatchIsCaseInsensitive()
    {
        var filter = new FileFilter(Config(extensions: [".PDF"]));
        FileInfo file = CreateFile("report.pdf", 1024);
        Assert.True(filter.ShouldIncludeFile(file));
    }

    private static FileInfo CreateFile(string name, long length)
    {
        // FileInfo with a fake path; we only need Name, Extension, Length for filtering logic
        // Use a temp path format so FileInfo doesn't try to stat a real file
        string dir = Path.GetTempPath();
        var info = new FileInfo(Path.Combine(dir, name));
        // We can't set Length on FileInfo directly — instead test using actual temp files
        using FileStream tmp = File.Create(info.FullName);
        tmp.SetLength(length);
        tmp.Flush();
        return new FileInfo(info.FullName);
    }

    private static DirectoryInfo CreateDir(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(path);
        return new DirectoryInfo(path);
    }
}
