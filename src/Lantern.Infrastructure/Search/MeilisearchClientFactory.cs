using System.Net.Http.Headers;
using Meilisearch;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Returns cached <see cref="MeilisearchClient"/> instances bound to the
/// credential of the requested role. Clients are created lazily on first
/// access and reused for the lifetime of the factory.
/// <para>
/// Credentials are resolved at <em>request</em> time, not at client creation
/// time. Singletons (and hosted services) that depend on a client are
/// constructed before any <c>IHostedService.StartAsync</c> runs, i.e. before
/// <see cref="IMeilisearchCredentialManager.EnsureCredentialsAsync"/> has
/// completed, so the client must not touch the credential manager until it
/// actually sends a request.
/// </para>
/// </summary>
public sealed class MeilisearchClientFactory : IMeilisearchClientFactory
{
    private readonly MeilisearchOptions _options;
    private readonly IMeilisearchCredentialManager _credentialManager;
    private readonly object _gate = new();
    private readonly Dictionary<MeilisearchCredentialRole, MeilisearchClient> _clients = new();

    public MeilisearchClientFactory(
        MeilisearchOptions options,
        IMeilisearchCredentialManager credentialManager)
    {
        _options = options;
        _credentialManager = credentialManager;
    }

    public MeilisearchClient GetSearchClient() => Get(MeilisearchCredentialRole.Search);

    public MeilisearchClient GetIndexingClient() => Get(MeilisearchCredentialRole.Indexing);

    public MeilisearchClient GetAdminClient() => Get(MeilisearchCredentialRole.Admin);

    private MeilisearchClient Get(MeilisearchCredentialRole role)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(role, out MeilisearchClient? existing))
                return existing;

            var http = new HttpClient(new CredentialHandler(_credentialManager, role, new HttpClientHandler()))
            {
                BaseAddress = new Uri(_options.Url),
            };
            var client = new MeilisearchClient(http, "");
            _clients[role] = client;
            return client;
        }
    }

    /// <summary>Attaches the role's Bearer token to each outgoing request.</summary>
    private sealed class CredentialHandler(
        IMeilisearchCredentialManager credentialManager,
        MeilisearchCredentialRole role,
        HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            MeilisearchCredential credential = credentialManager.GetCredential(role);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.KeyValue);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
