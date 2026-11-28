namespace Lantern.Crawling.Files;

public sealed record DiscoveredFile(
    string CanonicalPath,
    long FileSize,
    DateTimeOffset LastWriteTimeUtc);
