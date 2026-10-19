using Lantern.Core.Search;

namespace Lantern.Core.Tests;

public sealed class DocumentNormalizationTests
{
    [Theory]
    [InlineData(@"C:\Docs\a.txt", "c:/docs/./a.txt")]
    [InlineData(@"\\server\share\a.txt", "//SERVER/share/folder/../a.txt")]
    public void FileIdentityIgnoresWindowsCaseAndSeparators(string a, string b) =>
        Assert.Equal(DocumentIdentity.Generate("file", "source", a), DocumentIdentity.Generate("file", "source", b));

    [Fact]
    public void IdentitySeparatesSourcesAndUnambiguousComponents()
    {
        Assert.NotEqual(DocumentIdentity.Generate("web", "ab", "https://example.com/c"), DocumentIdentity.Generate("web", "a", "https://example.com/bc"));
        Assert.NotEqual(DocumentIdentity.Generate("file", "a", @"C:\a.txt"), DocumentIdentity.Generate("file", "b", @"C:\a.txt"));
        Assert.Matches("^[0-9a-f]{64}$", DocumentIdentity.Generate("file", "a", @"C:\a.txt"));
    }

    [Fact]
    public void WebIdentityPreservesPathAndQuerySemantics()
    {
        string Id(string url) => DocumentIdentity.Generate("web", "source", url);
        Assert.Equal(Id("HTTPS://EXAMPLE.COM:443/a?q=1#one"), Id("https://example.com/a?q=1#two"));
        Assert.NotEqual(Id("https://example.com/A?q=1"), Id("https://example.com/a?q=1"));
        Assert.NotEqual(Id("https://example.com/a?q=1"), Id("https://example.com/a?q=2"));
        Assert.NotEqual(Id("https://example.com/a?a=1&b=2"), Id("https://example.com/a?b=2&a=1"));
    }

    [Fact]
    public void NormalizePreservesTextAndDisplayPathAndConvertsMetadata()
    {
        var doc = DocumentNormalizer.Normalize(new SearchDocument
        {
            SourceId = "files", SourceType = "file", Path = @"C:\Docs\日本語.TXT",
            Title = "  日本語 Title\0  ", Content = "日本語 English\r\n  punctuation！\t\0next\rline",
            MimeType = " TEXT/PLAIN ", Size = 10,
            ModifiedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(9))
        });
        Assert.Equal("日本語 Title", doc.Title);
        Assert.Equal("日本語 English\n  punctuation！\tnext\nline", doc.Content);
        Assert.Equal(@"C:\Docs\日本語.TXT", doc.Path);
        Assert.Equal(".txt", doc.Extension);
        Assert.Equal("text/plain", doc.MimeType);
        Assert.Equal(TimeSpan.Zero, doc.ModifiedAt!.Value.Offset);
        Assert.Equal(doc.Id, DocumentNormalizer.Normalize(doc).Id);
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("C:relative.txt")]
    [InlineData(@"\\server")]
    [InlineData(@"\\?\C:\a")]
    public void RejectsInvalidFileLocations(string path) => Assert.Throws<ArgumentException>(() => DocumentIdentity.FilePath(path));

    [Theory]
    [InlineData("ftp://example.com/a")]
    [InlineData("https://user:pass@example.com/a")]
    [InlineData("relative")]
    public void RejectsInvalidWebLocations(string uri) => Assert.Throws<ArgumentException>(() => DocumentIdentity.WebUri(uri));

    [Fact]
    public void RejectsInvalidMetadataAndWrongId()
    {
        Assert.Throws<ArgumentException>(() => DocumentNormalizer.Normalize(new SearchDocument { Title = " ", Content = "" }));
        Assert.Throws<ArgumentException>(() => DocumentNormalizer.Normalize(new SearchDocument
        { SourceId = "web", SourceType = "web", Uri = "https://example.com", Title = "title", Content = "", Id = "wrong" }));
        Assert.Throws<ArgumentException>(() => DocumentNormalizer.Normalize(new SearchDocument
        { SourceId = "web", SourceType = "web", Uri = "https://example.com", Title = "title", Content = "", Size = -1 }));
    }
}
