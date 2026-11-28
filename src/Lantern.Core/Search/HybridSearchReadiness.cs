namespace Lantern.Core.Search;

/// <summary>Tracks startup verification when Lantern hosts the REST embedder.</summary>
public sealed class HybridSearchReadiness
{
    private volatile bool _ready;
    public bool IsReady => _ready;
    public void SetReady(bool ready) => _ready = ready;
}
