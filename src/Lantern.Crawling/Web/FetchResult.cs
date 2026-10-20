namespace Lantern.Crawling.Web;

public enum FetchStatus
{
    Ok,
    NotModified,
    NotFound,
    Gone,
    Forbidden,
    Unauthorized,
    TooManyRequests,
    ServerError,
    Redirect,
    UnsupportedMediaType,
    TooLarge,
    NetworkError,
    SsrfBlocked,
    Skipped
}

public sealed class FetchResult
{
    public FetchStatus Status { get; init; }
    public int HttpStatusCode { get; init; }
    public string? ContentType { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public long ContentLength { get; init; }
    public string? RedirectUrl { get; init; }
    public byte[]? Body { get; init; }
    public string? ErrorMessage { get; init; }
    public int? RetryAfterSeconds { get; init; }

    public bool IsSuccess => Status == FetchStatus.Ok;
    public bool IsNotModified => Status == FetchStatus.NotModified;
    public bool IsGone => Status is FetchStatus.NotFound or FetchStatus.Gone;
    public bool IsTransientFailure => Status is FetchStatus.TooManyRequests or FetchStatus.ServerError or FetchStatus.NetworkError;

    public static FetchResult Error(FetchStatus status, string message, int httpCode = 0) =>
        new() { Status = status, ErrorMessage = message, HttpStatusCode = httpCode };
}
