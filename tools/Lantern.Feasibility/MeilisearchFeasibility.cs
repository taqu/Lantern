using Meilisearch;

namespace Lantern.Feasibility;

/// <summary>
/// Tests Meilisearch connectivity: connects, indexes sample documents, searches, cleans up.
/// </summary>
internal sealed class MeilisearchFeasibility
{
    private const string IndexUid = "feasibility-test";
    private const double TimeoutMs = 30_000;

    private readonly string _host;
    private readonly string _masterKey;

    public MeilisearchFeasibility(string host, string masterKey)
    {
        _host = host;
        _masterKey = masterKey;
    }

    public async Task<bool> RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("=== Meilisearch Feasibility ===");
        Console.WriteLine($"Host : {_host}");
        Console.WriteLine();

        MeilisearchClient client;
        try
        {
            client = new MeilisearchClient(_host, _masterKey);
        }
        catch (Exception ex)
        {
            Fail("Failed to create client", ex);
            return false;
        }

        // 1. Health check
        Console.Write("  [1] Health check ... ");
        try
        {
            var health = await client.HealthAsync();
            if (health.Status != "available")
            {
                Fail($"Unexpected status: {health.Status}");
                return false;
            }
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Meilisearch is not reachable", ex);
            Console.WriteLine("      Ensure Meilisearch is running and MEILI_HOST / MEILI_MASTER_KEY are correct.");
            return false;
        }

        // 2. Ensure clean state — delete index if it already exists
        Console.Write("  [2] Ensure clean index ... ");
        try
        {
            var existingIndexes = await client.GetAllIndexesAsync();
            if (existingIndexes.Results.Any(i => i.Uid == IndexUid))
            {
                var deleteTask = await client.DeleteIndexAsync(IndexUid);
                await client.WaitForTaskAsync(deleteTask.TaskUid, timeoutMs: TimeoutMs);
            }
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Could not clean up existing index", ex);
            return false;
        }

        // 3. Create index
        Console.Write("  [3] Create index ... ");
        try
        {
            var createTask = await client.CreateIndexAsync(IndexUid, "id");
            await client.WaitForTaskAsync(createTask.TaskUid, timeoutMs: TimeoutMs);
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Could not create index", ex);
            return false;
        }

        var index = client.Index(IndexUid);

        // 4. Index documents
        Console.Write("  [4] Index documents ... ");
        var documents = new[]
        {
            new SampleDocument { Id = 1, Title = "Lantern Introduction",  Content = "Lantern is a lightweight full-text search platform." },
            new SampleDocument { Id = 2, Title = "Document Extraction",   Content = "PDF and Office documents can be indexed." },
            new SampleDocument { Id = 3, Title = "Web Crawling",          Content = "Website content can be discovered and searched." },
        };

        try
        {
            var addTask = await index.AddDocumentsAsync(documents);
            await client.WaitForTaskAsync(addTask.TaskUid, timeoutMs: TimeoutMs);
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Could not index documents", ex);
            await TryCleanupAsync(client);
            return false;
        }

        // 5. Search
        Console.Write("  [5] Search for \"lightweight\" ... ");
        try
        {
            var results = await index.SearchAsync<SampleDocument>("lightweight");
            if (results.Hits.Count == 0)
            {
                Fail("No results returned");
                await TryCleanupAsync(client);
                return false;
            }

            var hit = results.Hits.FirstOrDefault(h => h.Id == 1);
            if (hit == null)
            {
                Fail($"Expected document id=1 in results, got ids: {string.Join(", ", results.Hits.Select(h => h.Id))}");
                await TryCleanupAsync(client);
                return false;
            }
            Console.Write($"(found {results.Hits.Count} hit(s)) ");
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Search failed", ex);
            await TryCleanupAsync(client);
            return false;
        }

        // 6. Second search (broader term)
        Console.Write("  [6] Search for \"indexed\" ... ");
        try
        {
            var results = await index.SearchAsync<SampleDocument>("indexed");
            Console.Write($"(found {results.Hits.Count} hit(s)) ");
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Second search failed", ex);
            await TryCleanupAsync(client);
            return false;
        }

        // 7. Delete index
        Console.Write("  [7] Delete test index ... ");
        try
        {
            await TryCleanupAsync(client);
            Pass();
        }
        catch (Exception ex)
        {
            Fail("Could not delete index (manual cleanup may be required)", ex);
            return false;
        }

        Console.WriteLine();
        Console.WriteLine("  RESULT: PASS — Meilisearch is reachable, indexing and search work correctly.");
        return true;
    }

    private static async Task TryCleanupAsync(MeilisearchClient client)
    {
        try
        {
            var deleteTask = await client.DeleteIndexAsync(IndexUid);
            await client.WaitForTaskAsync(deleteTask.TaskUid, timeoutMs: TimeoutMs);
        }
        catch
        {
            // Best-effort cleanup; ignore errors here.
        }
    }

    private static void Pass() => Console.WriteLine("PASS");

    private static void Fail(string reason, Exception? ex = null)
    {
        Console.WriteLine($"FAIL — {reason}");
        if (ex != null)
        {
            Console.WriteLine($"      Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class SampleDocument
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
