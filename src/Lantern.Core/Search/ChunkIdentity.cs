using System.Security.Cryptography;
using System.Text.Json;

namespace Lantern.Core.Search;

public static class ChunkIdentity
{
    // ChunkId = SHA256(DocumentId + ChunkIndex)
    // Note: index-based IDs can change when earlier content is inserted.
    public static string Generate(string documentId, int chunkIndex)
    {
        var components = new[] { documentId, chunkIndex.ToString() };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(components);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
