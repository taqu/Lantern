namespace Lantern.Web;

/// <summary>
/// Configuration for Lantern-managed Meilisearch process lifecycle.
/// When <see cref="Managed"/> is <c>false</c> (the default), Lantern connects to an
/// externally-managed Meilisearch instance and does not attempt to start one.
/// </summary>
public sealed class MeilisearchProcessOptions
{
    public const string SectionName = "MeilisearchProcess";

    /// <summary>Whether Lantern owns and manages the Meilisearch process.</summary>
    public bool Managed { get; set; } = false;

    /// <summary>
    /// Path to the Meilisearch executable. Relative paths are resolved from the
    /// application base directory (the directory containing Lantern.Web.exe).
    /// </summary>
    public string ExecutablePath { get; set; } = @"bin\meilisearch\meilisearch.exe";

    /// <summary>Absolute path to the Meilisearch persistent data directory.</summary>
    public string DataDirectory { get; set; } = "";

    /// <summary>Loopback address Meilisearch binds to.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>Port Meilisearch listens on. Must match the port in Meilisearch:Url.</summary>
    public int Port { get; set; } = 7700;

    /// <summary>
    /// Path to a file whose sole content is the Meilisearch master key.
    /// The key is kept out of command-line arguments in process listings via an
    /// environment variable instead of being passed directly to the process.
    /// </summary>
    public string MasterKeyFile { get; set; } = "";

    /// <summary>Seconds to wait for Meilisearch /health to return 200 after launch.</summary>
    public int StartupTimeoutSeconds { get; set; } = 60;

    /// <summary>Maximum automatic restart attempts after an unexpected crash.</summary>
    public int MaxRestartAttempts { get; set; } = 3;

    /// <summary>Seconds to wait between restart attempts.</summary>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>When true, failure to start Meilisearch prevents the application from starting.</summary>
    public bool FailOnStartupError { get; set; } = true;
}
