using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Drives <see cref="IMeilisearchCredentialManager.EnsureCredentialsAsync"/>
/// during host startup so that role-scoped clients are ready before any
/// search or indexing subsystem begins work. Must be registered <em>after</em>
/// the Meilisearch process manager so the server is already accepting
/// authenticated requests.
/// </summary>
public sealed class MeilisearchCredentialBootstrapper(
    IMeilisearchCredentialManager credentialManager,
    IMeilisearchClientFactory meilisearchClientFactory,
    ILogger<MeilisearchCredentialBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try {
            await credentialManager.EnsureCredentialsAsync(cancellationToken);
            await meilisearchClientFactory.EnsureClientsAsync(cancellationToken);
        } catch(Exception ex) {
            logger.LogError(ex, "Failed to ensure Meilisearch credentials during startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
