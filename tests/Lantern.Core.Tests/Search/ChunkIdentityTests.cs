using Lantern.Core.Search;

namespace Lantern.Core.Tests.Search;

public sealed class ChunkIdentityTests
{
    [Fact]
    public void Generate_ReturnsSha256HexString()
    {
        string id = ChunkIdentity.Generate("doc1", 0);
        Assert.Equal(64, id.Length); // SHA256 = 32 bytes = 64 hex chars
        Assert.Matches("^[0-9a-f]+$", id);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        string id1 = ChunkIdentity.Generate("doc-abc", 3);
        string id2 = ChunkIdentity.Generate("doc-abc", 3);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Generate_DifferentDocumentId_ReturnsDifferentId()
    {
        string id1 = ChunkIdentity.Generate("doc-a", 0);
        string id2 = ChunkIdentity.Generate("doc-b", 0);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Generate_DifferentChunkIndex_ReturnsDifferentId()
    {
        string id1 = ChunkIdentity.Generate("doc", 0);
        string id2 = ChunkIdentity.Generate("doc", 1);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Generate_EmptyDocumentId_Works()
    {
        string id = ChunkIdentity.Generate("", 0);
        Assert.Equal(64, id.Length);
    }
}
