namespace Lantern.Crawling.Files;

public sealed class EnumerationState
{
    public bool Completed { get; set; }
    public Exception? RootFailure { get; set; }
    public List<string> DirectoryErrors { get; } = [];
}
