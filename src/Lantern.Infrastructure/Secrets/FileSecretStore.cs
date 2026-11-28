using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Secrets;

/// <summary>
/// Default <see cref="ISecretStore"/> implementation: one file per secret name
/// inside <see cref="SecretStoreOptions.Directory"/>. On Linux and macOS each
/// file is restricted to <c>0600</c>. On Windows the containing directory ACL
/// (applied by <c>Lantern.Web setup</c>) is the enforcement mechanism.
/// </summary>
/// <remarks>
/// Writes are performed atomically via a temporary sibling file followed by
/// <see cref="File.Move(string, string, bool)"/> with overwrite, so a crash
/// during write cannot leave a truncated secret visible to readers.
/// </remarks>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _directory;

    public FileSecretStore(IOptions<SecretStoreOptions> options)
    {
        string configured = options.Value.Directory;
        _directory = string.IsNullOrEmpty(configured)
            ? Path.Combine(AppContext.BaseDirectory, "AppData", "secrets")
            : configured;
    }

    public async Task<string?> TryReadAsync(string name, CancellationToken cancellationToken)
    {
        string path = PathFor(name);
        if(!File.Exists(path)) return null;
        string content = await File.ReadAllTextAsync(path, cancellationToken);
        return content.Trim();
    }

    public async Task WriteAsync(string name, string value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(name);
        string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        await File.WriteAllTextAsync(tmp, value, cancellationToken);
        RestrictPermissions(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        string path = PathFor(name);
        if(File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        if(string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Secret name must be a non-empty filesystem-safe identifier.", nameof(name));
        return Path.Combine(_directory, name + ".txt");
    }

    private static void RestrictPermissions(string path)
    {
        if(!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
