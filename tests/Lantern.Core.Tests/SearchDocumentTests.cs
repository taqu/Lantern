using Lantern.Core.Search;

namespace Lantern.Core.Tests;

public sealed class SearchDocumentTests
{
    [Fact]
    public void SearchDocument_Properties_AreSetCorrectly()
    {
        var doc = new SearchDocument
        {
            Id = "doc-1",
            Title = "Test Title",
            Content = "Test content for unit testing.",
            SourcePath = "/path/to/file.txt"
        };

        Assert.Equal("doc-1", doc.Id);
        Assert.Equal("Test Title", doc.Title);
        Assert.Equal("Test content for unit testing.", doc.Content);
        Assert.Equal("/path/to/file.txt", doc.SourcePath);
        Assert.True(doc.IndexedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void SearchDocument_SourcePath_IsOptional()
    {
        var doc = new SearchDocument
        {
            Id = "doc-2",
            Title = "No Path",
            Content = "Content without a source path."
        };

        Assert.Null(doc.SourcePath);
    }

    [Fact]
    public void SearchResult_Properties_AreSetCorrectly()
    {
        var hits = new List<SearchDocument>
        {
            new() { Id = "1", Title = "Hit One", Content = "First result." },
            new() { Id = "2", Title = "Hit Two", Content = "Second result." }
        };

        var result = new SearchResult
        {
            IndexUid = "test-index",
            Hits = hits,
            TotalHits = 2,
            ProcessingTimeMs = 42,
            Query = "result"
        };

        Assert.Equal("test-index", result.IndexUid);
        Assert.Equal(2, result.Hits.Count);
        Assert.Equal(42, result.ProcessingTimeMs);
        Assert.Equal("result", result.Query);
    }

    [Fact]
    public void SearchResult_EmptyHits_IsValid()
    {
        var result = new SearchResult
        {
            IndexUid = "empty-index",
            Hits = Array.Empty<SearchDocument>(),
            TotalHits = 0,
            ProcessingTimeMs = 1,
            Query = "notfound"
        };

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.TotalHits);
    }
}
