namespace Lantern.Infrastructure.Secrets;

/// <summary>
/// Configuration for the <see cref="FileSecretStore"/>: the directory that
/// contains one file per secret name. Directory-level protection (Windows ACL
/// or Linux 0700) is applied by the installer; the store itself only enforces
/// per-file 0600 on Unix-like systems.
/// </summary>
public sealed class SecretStoreOptions
{
    public const string SectionName = "SecretStore";

    /// <summary>
    /// Absolute path to the directory that holds secret files. When empty, the
    /// store falls back to <c>Path.Combine(AppContext.BaseDirectory, "AppData",
    /// "secrets")</c>, which matches the dev layout.
    /// </summary>
    public string Directory { get; set; } = "";
}
