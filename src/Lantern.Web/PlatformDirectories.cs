namespace Lantern.Web;

/// <summary>
/// Platform-appropriate default paths for Lantern configuration, secrets, and persistent data.
/// All properties are computed once at startup from OS-specific conventions.
///
/// Windows  : %ProgramData%\Lantern
/// Linux    : /etc/lantern  (config/secrets)  and  /var/lib/lantern  (data)
/// </summary>
public static class PlatformDirectories
{
    /// <summary>
    /// Production configuration file.
    ///   Windows  : %ProgramData%\Lantern\config\appsettings.json
    ///   Linux    : /etc/lantern/appsettings.json
    /// </summary>
    public static string ProductionConfigFile { get; } = LocateProductionConfig();

    /// <summary>
    /// Default persistent data root for databases, indexes, and backups.
    ///   Windows  : %ProgramData%\Lantern
    ///   Linux    : /var/lib/lantern
    /// </summary>
    public static string DefaultDataRoot { get; } = LocateDataRoot();

    /// <summary>
    /// Default secrets directory for master keys and scoped API keys.
    ///   Windows  : %ProgramData%\Lantern\secrets
    ///   Linux    : /etc/lantern/secrets
    /// </summary>
    public static string DefaultSecretsDir { get; } = LocateSecretsDir();

    private static string LocateDataRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lantern")
            : "/var/lib/lantern";

    private static string LocateProductionConfig() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lantern", "config", "appsettings.json")
            : "/etc/lantern/appsettings.json";

    private static string LocateSecretsDir() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lantern", "secrets")
            : "/etc/lantern/secrets";
}
