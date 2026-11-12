using Lantern.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

/// <summary>
/// Unit tests for <see cref="FileSecretStore"/>: round-trip semantics, missing
/// entries, atomic replacement, invalid names, and Unix 0600 enforcement.
/// </summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "secret-store-test-" + Guid.NewGuid().ToString("N"));

    private FileSecretStore NewStore() =>
        new(Options.Create(new SecretStoreOptions { Directory = _dir }));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task WriteAndRead_RoundTripsValue()
    {
        FileSecretStore store = NewStore();
        await store.WriteAsync("role-key", "abc123", CancellationToken.None);
        Assert.Equal("abc123", await store.TryReadAsync("role-key", CancellationToken.None));
    }

    [Fact]
    public async Task TryRead_MissingSecret_ReturnsNull()
    {
        FileSecretStore store = NewStore();
        Assert.Null(await store.TryReadAsync("not-present", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesExistingSecret()
    {
        FileSecretStore store = NewStore();
        await store.WriteAsync("doomed", "value", CancellationToken.None);
        await store.DeleteAsync("doomed", CancellationToken.None);
        Assert.Null(await store.TryReadAsync("doomed", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_MissingSecret_IsNoOp()
    {
        FileSecretStore store = NewStore();
        await store.DeleteAsync("never-existed", CancellationToken.None);
    }

    [Fact]
    public async Task Write_OverwritesPreviousValueAtomically()
    {
        FileSecretStore store = NewStore();
        await store.WriteAsync("rotating", "old-key", CancellationToken.None);
        await store.WriteAsync("rotating", "new-key", CancellationToken.None);
        Assert.Equal("new-key", await store.TryReadAsync("rotating", CancellationToken.None));

        // No orphaned .tmp sibling should remain after a successful write.
        string[] tmpResidue = Directory.GetFiles(_dir, "*.tmp-*");
        Assert.Empty(tmpResidue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public async Task InvalidName_Throws(string badName)
    {
        FileSecretStore store = NewStore();
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(badName, "x", CancellationToken.None));
    }

    [Fact]
    public async Task TryRead_TrimsTrailingWhitespace()
    {
        // Writes are passed through verbatim, but reads normalize to allow
        // operators to drop a key into the file with a trailing newline.
        FileSecretStore store = NewStore();
        await store.WriteAsync("padded", "value-with-newline\n", CancellationToken.None);
        Assert.Equal("value-with-newline", await store.TryReadAsync("padded", CancellationToken.None));
    }

    [SkippableFact]
    public async Task Write_OnUnix_AppliesOwnerOnlyPermissions()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "0600 enforcement is Unix-only; Windows relies on directory ACL.");

        FileSecretStore store = NewStore();
        await store.WriteAsync("perm-check", "secret", CancellationToken.None);

        string path = Path.Combine(_dir, "perm-check.txt");
#pragma warning disable CA1416 // Guarded by Skip.IfNot above.
        UnixFileMode mode = File.GetUnixFileMode(path);
#pragma warning restore CA1416
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
