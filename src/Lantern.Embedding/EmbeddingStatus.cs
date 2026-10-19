namespace Lantern.Embedding;

public enum ModelStatus { NotLoaded, Loading, Ready, Failed, Stopping }

public sealed class EmbeddingServiceStatus
{
    private volatile ModelStatus _status = ModelStatus.NotLoaded;
    private string _message = "Not initialized";

    public ModelStatus Status => _status;
    public string Message => _message;
    public bool IsReady => _status == ModelStatus.Ready;

    public void SetLoading() { _status = ModelStatus.Loading; _message = "Loading model..."; }
    public void SetReady() { _status = ModelStatus.Ready; _message = "Ready"; }
    public void SetFailed(string msg) { _status = ModelStatus.Failed; _message = msg; }
    public void SetStopping() { _status = ModelStatus.Stopping; _message = "Stopping"; }
}
