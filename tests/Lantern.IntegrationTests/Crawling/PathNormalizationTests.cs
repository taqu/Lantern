using Lantern.Core.Search;

namespace Lantern.IntegrationTests.Crawling;

public sealed class PathNormalizationTests
{
    [Theory]
    [InlineData(@"C:\Documents", @"C:\Documents")]
    [InlineData(@"C:\Documents\", @"C:\Documents")]
    [InlineData(@"C:/Documents", @"C:\Documents")]
    [InlineData(@"C:\Documents\SubDir\..\SubDir", @"C:\Documents\SubDir")]
    public void DriveLetterPathsNormalize(string input, string expected)
    {
        string result = DocumentIdentity.FilePath(input);
        Assert.Equal(expected, result, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"//server/share", @"\\server\share")]
    public void UncPathsNormalize(string input, string expected)
    {
        string result = DocumentIdentity.FilePath(input);
        Assert.Equal(expected, result, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData(@"\\")]
    [InlineData(@"\\server")]
    public void InvalidPathsThrow(string input)
    {
        Assert.Throws<ArgumentException>(() => DocumentIdentity.FilePath(input));
    }

    [Fact]
    public void DocumentIdIsDeterministicForSameFile()
    {
        string id1 = DocumentIdentity.Generate("file", "crawler-1", @"C:\docs\report.pdf");
        string id2 = DocumentIdentity.Generate("file", "crawler-1", @"C:\docs\report.pdf");
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void DocumentIdDiffersForDifferentPaths()
    {
        string id1 = DocumentIdentity.Generate("file", "crawler-1", @"C:\docs\a.pdf");
        string id2 = DocumentIdentity.Generate("file", "crawler-1", @"C:\docs\b.pdf");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void DocumentIdDiffersForDifferentCrawlers()
    {
        string id1 = DocumentIdentity.Generate("file", "crawler-1", @"C:\docs\a.pdf");
        string id2 = DocumentIdentity.Generate("file", "crawler-2", @"C:\docs\a.pdf");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void DocumentIdIsCaseInsensitiveForWindows()
    {
        string id1 = DocumentIdentity.Generate("file", "crawler-1", @"C:\DOCS\report.pdf");
        string id2 = DocumentIdentity.Generate("file", "crawler-1", @"c:\docs\REPORT.pdf");
        Assert.Equal(id1, id2);
    }
}
