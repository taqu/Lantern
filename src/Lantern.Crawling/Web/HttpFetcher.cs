using System.Net;
using System.Net.Sockets;
using Lantern.Core.Crawling;
using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Web;

/// <summary>
/// Performs bounded, SSRF-protected HTTP requests.
/// Disables automatic redirects; redirects are processed explicitly by the caller.
/// DNS validation occurs at connection time via SocketsHttpHandler.ConnectCallback
/// to prevent DNS rebinding attacks.
/// </summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly HttpClient _client;
    private readonly CrawlBoundaryValidator _validator;
    private readonly int _maxBodyBytes;
    private readonly ILogger _logger;

    public HttpFetcher(
        WebCrawlerConfiguration config,
        WebCrawlingOptions options,
        CrawlBoundaryValidator validator,
        ILogger logger)
    {
        _validator = validator;
        _maxBodyBytes = config.MaximumResponseSizeMB * 1024 * 1024;
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxAutomaticRedirections = 0,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            ConnectCallback = async (ctx, ct) =>
                await ValidatedConnectAsync(ctx, config.AllowedHosts, ct)
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
        };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en,ja;q=0.8");
    }

    public HttpClient HttpClient => _client;

    public async Task<FetchResult> FetchAsync(string url, string? etag, string? lastModified, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        else if (!string.IsNullOrEmpty(lastModified))
            request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return FetchResult.Error(FetchStatus.NetworkError, "Connection refused.");
        }
        catch (HttpRequestException ex)
        {
            return FetchResult.Error(FetchStatus.NetworkError, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return FetchResult.Error(FetchStatus.NetworkError, "Request timed out.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("blocked"))
        {
            return FetchResult.Error(FetchStatus.SsrfBlocked, ex.Message);
        }

        using (response)
        {
            return await BuildResultAsync(response, ct);
        }
    }

    private async Task<FetchResult> BuildResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        int code = (int)response.StatusCode;
        string? etag = response.Headers.ETag?.ToString();
        string? lastModified = response.Content.Headers.LastModified?.ToString("R");
        string? contentType = response.Content.Headers.ContentType?.MediaType;

        FetchStatus status = code switch
        {
            304 => FetchStatus.NotModified,
            301 or 302 or 303 or 307 or 308 => FetchStatus.Redirect,
            401 => FetchStatus.Unauthorized,
            403 => FetchStatus.Forbidden,
            404 => FetchStatus.NotFound,
            410 => FetchStatus.Gone,
            429 => FetchStatus.TooManyRequests,
            >= 400 and < 500 => FetchStatus.Skipped,
            >= 500 => FetchStatus.ServerError,
            200 or 201 => FetchStatus.Ok,
            _ => FetchStatus.Skipped
        };

        string? redirectUrl = null;
        if (status == FetchStatus.Redirect)
        {
            redirectUrl = response.Headers.Location?.ToString();
        }

        int? retryAfter = null;
        if (status == FetchStatus.TooManyRequests && response.Headers.RetryAfter?.Delta.HasValue == true)
            retryAfter = (int)response.Headers.RetryAfter.Delta!.Value.TotalSeconds;

        if (status != FetchStatus.Ok)
        {
            return new FetchResult
            {
                Status = status,
                HttpStatusCode = code,
                ContentType = contentType,
                ETag = etag,
                LastModified = lastModified,
                RedirectUrl = redirectUrl,
                RetryAfterSeconds = retryAfter
            };
        }

        // Read body with size limit
        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength > _maxBodyBytes)
            return FetchResult.Error(FetchStatus.TooLarge, $"Response too large: {contentLength / 1024 / 1024} MB");

        byte[]? body = null;
        try
        {
            using var limited = new LimitedStream(await response.Content.ReadAsStreamAsync(ct), _maxBodyBytes);
            using var ms = new MemoryStream();
            await limited.CopyToAsync(ms, ct);
            if (limited.LimitExceeded)
                return FetchResult.Error(FetchStatus.TooLarge, $"Response exceeded {_maxBodyBytes / 1024 / 1024} MB limit");
            body = ms.ToArray();
        }
        catch (Exception ex)
        {
            return FetchResult.Error(FetchStatus.NetworkError, $"Failed to read body: {ex.Message}");
        }

        return new FetchResult
        {
            Status = FetchStatus.Ok,
            HttpStatusCode = code,
            ContentType = contentType,
            ETag = etag,
            LastModified = lastModified,
            ContentLength = body.Length,
            Body = body
        };
    }

    private async Task<Stream> ValidatedConnectAsync(
        SocketsHttpConnectionContext ctx,
        IReadOnlyList<string> allowedHosts,
        CancellationToken ct)
    {
        string host = ctx.DnsEndPoint.Host;
        if (!CrawlBoundaryValidator.IsHostAllowed(host, allowedHosts))
            throw new InvalidOperationException($"Host '{host}' blocked: not in allowed hosts list.");

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, ct);
        foreach (IPAddress ip in addresses)
        {
            if (IPAddress.IsLoopback(ip))
                throw new InvalidOperationException($"Host '{host}' blocked: resolves to loopback {ip}.");

            if (IsAlwaysBlockedIp(ip))
                throw new InvalidOperationException($"Host '{host}' blocked: resolves to restricted address {ip}.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(ctx.DnsEndPoint, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsAlwaysBlockedIp(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 0xfe && (b[1] & 0xc0) == 0x80; // link-local fe80::/10
        }
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] addr = ip.GetAddressBytes();
        // 169.254.0.0/16 (link-local / metadata)
        if (addr[0] == 169 && addr[1] == 254) return true;
        // 100.64.0.0/10 (CGNAT / AWS instance metadata)
        if (addr[0] == 100 && (addr[1] & 0xC0) == 64) return true;
        // 0.0.0.0
        if (addr[0] == 0) return true;
        // 240.0.0.0/4 reserved
        if ((addr[0] & 0xF0) == 240) return true;
        return false;
    }

    public void Dispose() => _client.Dispose();

    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;
        public bool LimitExceeded { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_read >= limit) { LimitExceeded = true; return 0; }
            int toRead = (int)Math.Min(count, limit - _read);
            int n = await inner.ReadAsync(buffer.AsMemory(offset, toRead), ct);
            _read += n;
            if (_read >= limit && n == toRead) LimitExceeded = true;
            return n;
        }
    }
}
