namespace Lantern.Embedding;

/// <summary>
/// Embedding Options
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public bool Enabled { get; set; } = false;
    public string ModelPath { get; set; } = "resources/granite-embedding-97M-multilingual-r2.gguf";
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7701;
    public Uri Endpoint => new UriBuilder(Uri.UriSchemeHttp, ListenAddress, Port, "/embed").Uri;
    public uint ContextSize { get; set; } = 4096;
    public int Threads { get; set; } = 4;
    public int InferenceWorkers { get; set; } = 1;
    public int QueueCapacity { get; set; } = 32;
    public int RequestTimeoutSeconds { get; set; } = 60;

    public string? GetValidationError()
    {
        if(string.IsNullOrWhiteSpace(ModelPath)) return "ModelPath is required";
        if(Uri.CheckHostName(ListenAddress) == UriHostNameType.Unknown) return "Invalid ListenAddress";
        if(Port is <= 0 or > 65535) return "Invalid Port";
        if(ContextSize == 0 || Threads <= 0) return "ContextSize and Threads must be positive";
        if(QueueCapacity <= 0 || RequestTimeoutSeconds <= 0) return "QueueCapacity and RequestTimeoutSeconds must be positive";
        return null;
    }
}
