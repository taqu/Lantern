namespace Lantern.Core.Administration;

public sealed record SearchEngineStatus(bool Available, string IndexName, long? DocumentCount, string Message);

public interface ISearchEngineHealthService
{
    Task<SearchEngineStatus> GetAsync(bool refresh = false, CancellationToken cancellationToken = default);
}
