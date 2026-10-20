using Lantern.Core.Administration;
using Meilisearch;

namespace Lantern.Infrastructure.Search;

public sealed class MeilisearchHealthService(MeilisearchClient client, MeilisearchOptions options) : ISearchEngineHealthService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SearchEngineStatus? _cached;
    private DateTimeOffset _expires;

    public async Task<SearchEngineStatus> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cached != null && DateTimeOffset.UtcNow < _expires) return _cached;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                MeiliSearchHealth health = await client.HealthAsync(deadline.Token);
                if (health.Status != "available") _cached = new(false, options.IndexName, null, "The search engine reports an unhealthy state.");
                else
                {
                    try
                    {
                        IndexStats stats = await client.Index(options.IndexName).GetStatsAsync(deadline.Token);
                        _cached = new(true, options.IndexName, stats.NumberOfDocuments, "Connected");
                    }
                    catch (MeilisearchApiError ex) when (ex.Code == "index_not_found")
                    { _cached = new(true, options.IndexName, null, "Connected; the configured index does not exist yet."); }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    { _cached = new(true, options.IndexName, null, "Connected; the configured index does not exist yet."); }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { _cached = new(false, options.IndexName, null, "The connection test timed out."); }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            { _cached = new(false, options.IndexName, null, "The search engine rejected the request. Check server credentials and permissions."); }
            catch (HttpRequestException) { _cached = new(false, options.IndexName, null, "Could not reach the search engine."); }
            catch (MeilisearchCommunicationError) { _cached = new(false, options.IndexName, null, "Could not reach the search engine."); }
            catch (MeilisearchTimeoutError) { _cached = new(false, options.IndexName, null, "The connection test timed out."); }
            catch (MeilisearchApiError) { _cached = new(false, options.IndexName, null, "The search engine rejected the request. Check server credentials and permissions."); }
            _expires = DateTimeOffset.UtcNow.AddSeconds(10);
            return _cached!;
        }
        finally { _gate.Release(); }
    }
}
