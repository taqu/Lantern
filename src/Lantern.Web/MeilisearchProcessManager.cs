using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Lantern.Web;

/// <summary>
/// Manages the lifecycle of the bundled Meilisearch process when
/// <see cref="MeilisearchProcessOptions.Managed"/> is enabled. API key
/// provisioning is delegated to
/// <see cref="Lantern.Infrastructure.Search.IMeilisearchCredentialManager"/>;
/// this type is responsible only for starting and supervising the server.
/// </summary>
public sealed class MeilisearchProcessManager(
    IOptions<MeilisearchProcessOptions> options,
    ILogger<MeilisearchProcessManager> logger)
    : IHostedService, IDisposable
{
    private readonly MeilisearchProcessOptions _opts = options.Value;
    private Process? _process;
    private CancellationTokenSource? _monitorCts;
    private Task _monitorTask = Task.CompletedTask;
    private int _restartCount;
    private volatile bool _stopping;

    /// <summary>Whether the managed Meilisearch process is currently running and healthy.</summary>
    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if(!_opts.Managed) {
            logger.LogInformation("Meilisearch process management is disabled. Lantern expects an external instance at the configured URL.");
            if(!string.IsNullOrEmpty(_opts.AllowedIpNetworks))
                logger.LogInformation(
                    "Hybrid Search requires Meilisearch to be started with " +
                    "MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS={Networks} so the REST embedder on the " +
                    "loopback address is reachable. Set this environment variable before starting " +
                    "Meilisearch or use --experimental-allowed-ip-networks {Networks} on the command line.",
                    _opts.AllowedIpNetworks, _opts.AllowedIpNetworks);
            return;
        }

        logger.LogInformation("Starting managed Meilisearch process...");

        string exePath = ResolveExecutablePath();
        if(!File.Exists(exePath)) {
            string msg = $"Meilisearch executable not found: {exePath}";
            if(_opts.FailOnStartupError) throw new FileNotFoundException(msg);
            logger.LogError("{Message}", msg);
            return;
        }

        string masterKey = await ReadMasterKeyAsync(cancellationToken);
        if(string.IsNullOrEmpty(masterKey)) {
            string msg = $"Meilisearch master key could not be read from: {_opts.MasterKeyFile}";
            if(_opts.FailOnStartupError) throw new InvalidOperationException(msg);
            logger.LogError("{Message}", msg);
            return;
        }

        if(!string.IsNullOrEmpty(_opts.DataDirectory))
            Directory.CreateDirectory(_opts.DataDirectory);

        try {
            await LaunchAndWaitForReadinessAsync(exePath, masterKey, cancellationToken);
        } catch(Exception ex) {
            if(_opts.FailOnStartupError) throw;
            logger.LogError(ex, "Meilisearch failed to start. Search will be unavailable.");
            return;
        }

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorLoopAsync(_monitorCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _monitorCts?.Cancel();

        try { await _monitorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); } catch(OperationCanceledException) { } catch(TimeoutException) { }

        if(_process is { HasExited: false } proc) {
            logger.LogInformation("Stopping managed Meilisearch process (PID {Pid})...", proc.Id);
            try {
                proc.CloseMainWindow();
                if(!proc.WaitForExit(5000)) {
                    proc.Kill();
                    logger.LogWarning("Meilisearch process did not exit gracefully; it was terminated.");
                } else {
                    logger.LogInformation("Meilisearch process stopped.");
                }
            } catch(Exception ex) {
                logger.LogError(ex, "Error while stopping Meilisearch process.");
            }
        }
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _process?.Dispose();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string ResolveExecutablePath()
    {
        string path = _opts.ExecutablePath;
        if(Path.IsPathRooted(path)) return path;
        string appBase = AppContext.BaseDirectory;
        return Path.GetFullPath(path, appBase);
    }

    private async Task<string> ReadMasterKeyAsync(CancellationToken ct)
    {
        if(string.IsNullOrEmpty(_opts.MasterKeyFile)) return "";
        if(!File.Exists(_opts.MasterKeyFile)) return "";
        return (await File.ReadAllTextAsync(_opts.MasterKeyFile, ct)).Trim();
    }

    private async Task LaunchAndWaitForReadinessAsync(string exePath, string masterKey, CancellationToken ct)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Pass secrets via environment variables to avoid exposure in process listings.
        startInfo.Environment["MEILI_MASTER_KEY"] = masterKey;
        // Allow the loopback REST embedder URL (127.0.0.1) to be reached by Meilisearch.
        // Without this, Meilisearch 1.9+ SSRF protection rejects loopback addresses with
        // "bad uri: Rejected URI" on every embedding request.
        // Set both the env var and the CLI arg for reliability across platforms/versions.
        if(!string.IsNullOrEmpty(_opts.AllowedIpNetworks)) {
            startInfo.Environment["MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS"] = _opts.AllowedIpNetworks;
        }
        startInfo.ArgumentList.Add("--db-path");
        startInfo.ArgumentList.Add(_opts.DataDirectory);
        startInfo.ArgumentList.Add("--http-addr");
        startInfo.ArgumentList.Add($"{_opts.BindAddress}:{_opts.Port}");
        startInfo.ArgumentList.Add("--no-analytics");
        if(!string.IsNullOrEmpty(_opts.AllowedIpNetworks)) {
            startInfo.ArgumentList.Add("--experimental-allowed-ip-networks");
            startInfo.ArgumentList.Add(_opts.AllowedIpNetworks);
        }

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Meilisearch process.");

        logger.LogInformation(
            "Meilisearch process started (PID {Pid}). AllowedIpNetworks={Networks}. Waiting for readiness...",
            _process.Id,
            string.IsNullOrEmpty(_opts.AllowedIpNetworks) ? "(not set)" : _opts.AllowedIpNetworks);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_opts.StartupTimeoutSeconds));

        string healthUrl = $"http://{_opts.BindAddress}:{_opts.Port}/health";
        using HttpClient http = new HttpClient();

        while(!timeout.IsCancellationRequested) {
            if(_process.HasExited)
                throw new InvalidOperationException(
                    $"Meilisearch process exited unexpectedly with code {_process.ExitCode}.");

            await Task.Delay(2000, timeout.Token).ConfigureAwait(false);

            try {
                using var response = await http.GetAsync(healthUrl, timeout.Token).ConfigureAwait(false);
                if(response.IsSuccessStatusCode) {
                    logger.LogInformation("Meilisearch is ready on {BindAddress}:{Port}.", _opts.BindAddress, _opts.Port);
                    return;
                }
            } catch(HttpRequestException) { } catch(OperationCanceledException) when(!ct.IsCancellationRequested) { break; }
        }

        throw new TimeoutException(
            $"Meilisearch did not become ready within {_opts.StartupTimeoutSeconds} seconds.");
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested && !_stopping) {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); } catch(OperationCanceledException) { return; }

            if(_process is { HasExited: true } dead) {
                if(_stopping) return;

                logger.LogWarning("Meilisearch process (PID {Pid}) exited with code {Code}.",
                    dead.Id, dead.ExitCode);

                if(_restartCount >= _opts.MaxRestartAttempts) {
                    logger.LogError(
                        "Meilisearch has crashed {Count} time(s) and will not be restarted automatically.",
                        _restartCount);
                    return;
                }

                _restartCount++;
                logger.LogInformation("Restarting Meilisearch (attempt {Attempt}/{Max})...",
                    _restartCount, _opts.MaxRestartAttempts);

                try {
                    await Task.Delay(TimeSpan.FromSeconds(_opts.RestartDelaySeconds), ct).ConfigureAwait(false);
                    string masterKey = await ReadMasterKeyAsync(ct).ConfigureAwait(false);
                    await LaunchAndWaitForReadinessAsync(ResolveExecutablePath(), masterKey, ct).ConfigureAwait(false);
                } catch(Exception ex) when(!ct.IsCancellationRequested) {
                    logger.LogError(ex, "Meilisearch restart attempt {Attempt} failed.", _restartCount);
                }
            }
        }
    }
}
