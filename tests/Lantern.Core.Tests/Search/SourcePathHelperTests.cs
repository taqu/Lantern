using Lantern.Core.Search;

namespace Lantern.Core.Tests.Search;

public sealed class SourcePathHelperTests
{
    // -----------------------------------------------------------------------
    // GetParentDirectory — Windows paths
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Documents\Research\presentation.ppt", @"C:\Documents\Research")]
    [InlineData(@"C:\Documents\My Files\report.pdf", @"C:\Documents\My Files")]
    [InlineData(@"C:\書類\レポート\data.xlsx", @"C:\書類\レポート")]
    [InlineData(@"C:\folder\file.txt", @"C:\folder")]
    [InlineData(@"C:\UPPER\Lower\Mixed.txt", @"C:\UPPER\Lower")]
    [InlineData(@"C:\deep\a\b\c\d\file.txt", @"C:\deep\a\b\c\d")]
    public void GetParentDirectory_WindowsPaths_ReturnsParent(string path, string expected)
        => Assert.Equal(expected, SourcePathHelper.GetParentDirectory(path));

    [Theory]
    [InlineData(@"C:\file.txt", @"C:\")]
    public void GetParentDirectory_WindowsFileAtRoot_ReturnsDriveRoot(string path, string expected)
        => Assert.Equal(expected, SourcePathHelper.GetParentDirectory(path));

    [Fact]
    public void GetParentDirectory_WindowsDriveRoot_ReturnsNull()
        => Assert.Null(SourcePathHelper.GetParentDirectory(@"C:\"));

    // -----------------------------------------------------------------------
    // GetParentDirectory — UNC paths
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(@"\\fileserver\shared\reports\summary.pdf", @"\\fileserver\shared\reports")]
    [InlineData(@"\\fileserver\shared\folder\sub\file.txt", @"\\fileserver\shared\folder\sub")]
    [InlineData(@"\\srv\share\書類\ファイル.txt", @"\\srv\share\書類")]
    [InlineData(@"\\fileserver\shared\file.pdf", @"\\fileserver\shared")]
    public void GetParentDirectory_UncPaths_ReturnsParent(string path, string expected)
        => Assert.Equal(expected, SourcePathHelper.GetParentDirectory(path));

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    public void GetParentDirectory_UncShareRoot_ReturnsNull(string path)
        => Assert.Null(SourcePathHelper.GetParentDirectory(path));

    // -----------------------------------------------------------------------
    // GetParentDirectory — Linux paths
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("/home/user/documents/report.pdf", "/home/user/documents")]
    [InlineData("/home/user/My Files/file.txt", "/home/user/My Files")]
    [InlineData("/home/user/書類/ファイル.txt", "/home/user/書類")]
    [InlineData("/var/data/nested/deep/file.bin", "/var/data/nested/deep")]
    [InlineData("/home/user/file.txt", "/home/user")]
    public void GetParentDirectory_LinuxPaths_ReturnsParent(string path, string expected)
        => Assert.Equal(expected, SourcePathHelper.GetParentDirectory(path));

    [Fact]
    public void GetParentDirectory_LinuxRootFile_ReturnsRoot()
        => Assert.Equal("/", SourcePathHelper.GetParentDirectory("/file.txt"));

    [Fact]
    public void GetParentDirectory_LinuxRoot_ReturnsNull()
        => Assert.Null(SourcePathHelper.GetParentDirectory("/"));

    [Fact]
    public void GetParentDirectory_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SourcePathHelper.GetParentDirectory(null));
        Assert.Null(SourcePathHelper.GetParentDirectory(""));
    }

    // -----------------------------------------------------------------------
    // DecodeFileUri — file: URI → original path
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("file:///C:/Documents/file.pdf", @"C:\Documents\file.pdf")]
    [InlineData("file:///C:/Documents/My%20Files/report.pdf", @"C:\Documents\My Files\report.pdf")]
    [InlineData("file:///C:/書類/ファイル.pdf", @"C:\書類\ファイル.pdf")]
    [InlineData("file:///D:/data/archive.zip", @"D:\data\archive.zip")]
    public void DecodeFileUri_WindowsDriveUri_ReturnsWindowsPath(string uri, string expected)
        => Assert.Equal(expected, SourcePathHelper.DecodeFileUri(uri));

    [Theory]
    [InlineData("file://server/share/folder/file.pdf", @"\\server\share\folder\file.pdf")]
    [InlineData("file://srv/docs/My%20Folder/file.txt", @"\\srv\docs\My Folder\file.txt")]
    public void DecodeFileUri_UncUri_ReturnsUncPath(string uri, string expected)
        => Assert.Equal(expected, SourcePathHelper.DecodeFileUri(uri));

    [Theory]
    [InlineData("file:///home/user/documents/file.pdf", "/home/user/documents/file.pdf")]
    [InlineData("file:///home/user/My%20Files/report.txt", "/home/user/My Files/report.txt")]
    [InlineData("file:///home/user/書類/ファイル.txt", "/home/user/書類/ファイル.txt")]
    public void DecodeFileUri_LinuxUri_ReturnsLinuxPath(string uri, string expected)
        => Assert.Equal(expected, SourcePathHelper.DecodeFileUri(uri));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/file.pdf")]
    public void DecodeFileUri_NonFileUri_ReturnsNull(string? uri)
        => Assert.Null(SourcePathHelper.DecodeFileUri(uri));

    // -----------------------------------------------------------------------
    // IsWebResult
    // -----------------------------------------------------------------------

    [Fact]
    public void IsWebResult_WebSourceType_ReturnsTrue()
    {
        var item = MakeItem(sourceType: "web", url: "https://example.com");
        Assert.True(SourcePathHelper.IsWebResult(item));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("FILE")]
    [InlineData(null)]
    [InlineData("")]
    public void IsWebResult_NonWebSourceType_ReturnsFalse(string? sourceType)
    {
        var item = MakeItem(sourceType: sourceType, path: @"C:\file.txt");
        Assert.False(SourcePathHelper.IsWebResult(item));
    }

    // -----------------------------------------------------------------------
    // GetCopyPath
    // -----------------------------------------------------------------------

    [Fact]
    public void GetCopyPath_WebResult_ReturnsUrl()
    {
        var item = MakeItem(sourceType: "web", url: "https://example.com/page");
        Assert.Equal("https://example.com/page", SourcePathHelper.GetCopyPath(item));
    }

    [Fact]
    public void GetCopyPath_FileResultWithPath_ReturnsPath()
    {
        var item = MakeItem(sourceType: "file", path: @"C:\Documents\file.pdf");
        Assert.Equal(@"C:\Documents\file.pdf", SourcePathHelper.GetCopyPath(item));
    }

    [Fact]
    public void GetCopyPath_FileResultWithFileUri_DecodesPath()
    {
        // Chunk results have Url=file:/// and Path=null
        var item = MakeItem(sourceType: "file", url: "file:///C:/Documents/file.pdf", path: null);
        Assert.Equal(@"C:\Documents\file.pdf", SourcePathHelper.GetCopyPath(item));
    }

    [Fact]
    public void GetCopyPath_WebResult_NullUrl_ReturnsNull()
    {
        var item = MakeItem(sourceType: "web", url: null);
        Assert.Null(SourcePathHelper.GetCopyPath(item));
    }

    // -----------------------------------------------------------------------
    // GetCopyDirectory — web results never have directory
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://intranet.local/doc")]
    [InlineData("https://example.com/articles/overview?q=1#sec")]
    public void GetCopyDirectory_WebResult_ReturnsNull(string url)
    {
        var item = MakeItem(sourceType: "web", url: url);
        Assert.Null(SourcePathHelper.GetCopyDirectory(item));
    }

    [Fact]
    public void GetCopyDirectory_FileResult_ReturnsParent()
    {
        var item = MakeItem(sourceType: "file", path: @"C:\Documents\Research\file.pdf");
        Assert.Equal(@"C:\Documents\Research", SourcePathHelper.GetCopyDirectory(item));
    }

    [Fact]
    public void GetCopyDirectory_FileResultDriveRoot_ReturnsNull()
    {
        var item = MakeItem(sourceType: "file", path: @"C:\");
        Assert.Null(SourcePathHelper.GetCopyDirectory(item));
    }

    [Fact]
    public void GetCopyDirectory_UncResult_ReturnsParent()
    {
        var item = MakeItem(sourceType: "file", path: @"\\server\share\reports\file.pdf");
        Assert.Equal(@"\\server\share\reports", SourcePathHelper.GetCopyDirectory(item));
    }

    [Fact]
    public void GetCopyDirectory_UncShareRoot_ReturnsNull()
    {
        var item = MakeItem(sourceType: "file", path: @"\\server\share");
        Assert.Null(SourcePathHelper.GetCopyDirectory(item));
    }

    private static SearchPageItem MakeItem(
        string? sourceType = null,
        string? url = null,
        string? path = null) =>
        new SearchPageItem(
            Id: "test",
            Title: "Test",
            Url: url,
            Path: path,
            Snippet: null,
            DocumentType: null,
            SourceType: sourceType);
}
