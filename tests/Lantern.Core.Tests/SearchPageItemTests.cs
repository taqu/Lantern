using Lantern.Core.Search;

namespace Lantern.Core.Tests;

public sealed class SearchPageItemTests
{
    [Fact]
    public void SearchPageItem_RecordProperties_AreSetCorrectly()
    {
        var item = new SearchPageItem(
            Id: "doc-001",
            Title: "Test Title",
            Url: "https://example.com/page",
            Path: @"C:\Docs\file.pdf",
            Snippet: "A short snippet of text.",
            DocumentType: "PDF");

        Assert.Equal("doc-001", item.Id);
        Assert.Equal("Test Title", item.Title);
        Assert.Equal("https://example.com/page", item.Url);
        Assert.Equal(@"C:\Docs\file.pdf", item.Path);
        Assert.Equal("A short snippet of text.", item.Snippet);
        Assert.Equal("PDF", item.DocumentType);
    }

    [Fact]
    public void SearchPageItem_NullableFields_CanBeNull()
    {
        var item = new SearchPageItem(
            Id: "doc-002",
            Title: "Minimal",
            Url: null,
            Path: null,
            Snippet: null,
            DocumentType: null);

        Assert.Null(item.Url);
        Assert.Null(item.Path);
        Assert.Null(item.Snippet);
        Assert.Null(item.DocumentType);
    }

    // UrlValidator tests

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<h1>test</h1>", false)]
    [InlineData("file:///C:/docs/file.pdf", false)]
    [InlineData("ftp://files.example.com/doc.pdf", false)]
    public void IsSafeDisplayUrl_UnsafeUrls_ReturnFalse(string? url, bool expected)
        => Assert.Equal(expected, UrlValidator.IsSafeDisplayUrl(url));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("https://EXAMPLE.COM/path", true)]
    [InlineData("http://intranet.local/doc", true)]
    [InlineData("HTTP://INTRANET.LOCAL/DOC", true)]
    public void IsSafeDisplayUrl_SafeUrls_ReturnTrue(string url, bool expected)
        => Assert.Equal(expected, UrlValidator.IsSafeDisplayUrl(url));
}
