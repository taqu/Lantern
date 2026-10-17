namespace Lantern.Infrastructure.Search;

/// <summary>
/// Thrown when a search operation fails due to a backend error.
/// </summary>
public sealed class SearchException : Exception
{
    public SearchException(string message, Exception? inner = null)
        : base(message, inner) { }
}
