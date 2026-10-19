using System.Text.Json;

namespace Lantern.Infrastructure.Search;

public static class DocumentBatcher
{
    // The SDK uses camel-case System.Text.Json serialization.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IEnumerable<IReadOnlyList<T>> Split<T>(IEnumerable<T> items, int maxCount, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxCount <= 0 || maxBytes < 2) throw new ArgumentOutOfRangeException(nameof(maxCount));
        var batch = new List<T>();
        long bytes = 2;
        foreach (var item in items)
        {
            var size = JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions).Length;
            if ((long)size + 2 > maxBytes) throw new ArgumentException($"An individual item exceeds the {maxBytes}-byte batch payload limit.");
            if (batch.Count == maxCount || bytes + size + (batch.Count == 0 ? 0 : 1) > maxBytes)
            {
                yield return batch;
                batch = new List<T>();
                bytes = 2;
            }
            bytes += size + (batch.Count == 0 ? 0 : 1);
            batch.Add(item);
        }
        if (batch.Count != 0) yield return batch;
    }
}
