using System.Net;
using System.Text.RegularExpressions;
using Lantern.Core.Administration;
using Lantern.Web.Administration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests;

public sealed class AdministrationHttpTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lantern-http-test-" + Guid.NewGuid().ToString("N"));
    private AdministrationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new AdministrationFactory(_directory);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await _factory.Services.GetRequiredService<AdministratorAuthentication>().ProvisionAsync("admin", "http-test-long-password");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }

    private async Task<string> TokenAsync(string path)
    {
        string html = await _client.GetStringAsync(path);
        string token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        return token;
    }

    private Task<HttpResponseMessage> PostAsync(string path, string token, params (string, string)[] values) =>
        _client.PostAsync(path, new FormUrlEncodedContent(values.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2))
            .Append(new("__RequestVerificationToken", token))));

    private async Task<HttpResponseMessage> LoginAsync(string password = "http-test-long-password") =>
        await PostAsync("/account/sign-in", await TokenAsync("/account/login"), ("username", "admin"), ("password", password));

    [Fact]
    public async Task AnonymousAdminAndDirectActionsAreDeniedWhileSearchIsPublic()
    {
        foreach (string? path in new[] { "/admin", "/admin/crawlers", "/admin/jobs", "/admin/index", "/admin/settings", "/admin/system" })
        {
            HttpResponseMessage response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://localhost/account/login", response.Headers.Location!.ToString());
        }
        foreach (string? path in new[] { "/admin/actions/settings", "/admin/actions/logout", "/admin/actions/test-connection" })
        {
            HttpResponseMessage response = await _client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/search")).StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ServiceProvider.GetRequiredService<AdministrationService>()
            .SaveSettingsAsync(new ApplicationSetting { DisplayName = "unauthorized" }));
    }

    [Fact]
    public async Task SettingsAccountAndAuthenticationKeysSurviveHostRestart()
    {
        HttpResponseMessage login = await LoginAsync();
        string cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Lantern.Admin="));
        await PostAsync("/admin/actions/settings", await TokenAsync("/admin/settings"), ("displayName", "Persistent Portal"));
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new AdministrationFactory(_directory);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/admin/settings")).StatusCode);
        Assert.Equal("Persistent Portal", (await _factory.Services.GetRequiredService<IApplicationSettingsService>().GetAsync()).DisplayName);
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndIsRateLimited()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        string token = await TokenAsync("/account/login");
        HttpResponseMessage? response = null;
        for (int i = 0; i < 10; i++)
        {
            response?.Dispose();
            response = await PostAsync("/account/sign-in", token, ("username", "admin"), ("password", "wrong"));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        response.Dispose();
    }

    [Fact]
    public async Task AuthorizationPolicyRequiresAuthenticatedAdministratorRole()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        Microsoft.AspNetCore.Authorization.IAuthorizationService authorization = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var ordinaryUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "ordinary") }, "test"));
        Assert.False((await authorization.AuthorizeAsync(ordinaryUser, null, AdministratorAuthentication.Policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(), null, AdministratorAuthentication.Policy)).Succeeded);
    }

    [Fact]
    public async Task LoginSettingsAntiforgeryLogoutAndCookieReplay()
    {
        Assert.Equal("/account/login?invalid=true", (await LoginAsync("wrong")).Headers.Location!.ToString());
        HttpResponseMessage login = await LoginAsync();
        Assert.Equal("/admin", login.Headers.Location!.ToString());
        string cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Lantern.Admin="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        foreach (string? path in new[] { "/admin", "/admin/crawlers", "/admin/jobs", "/admin/index", "/admin/settings", "/admin/system" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        string settingsHtml = await _client.GetStringAsync("/admin/settings");
        Assert.DoesNotContain("PasswordHash", settingsHtml);
        Assert.DoesNotContain("http-test-long-password", settingsHtml);
        string? key = System.Environment.GetEnvironmentVariable("MEILI_API_KEY");
        Assert.False(!string.IsNullOrEmpty(key) && settingsHtml.Contains(key, StringComparison.Ordinal));
        HttpResponseMessage noCsrf = await _client.PostAsync("/admin/actions/settings", new FormUrlEncodedContent(new Dictionary<string, string> { ["displayName"] = "CSRF" }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("/admin/settings?saved=true", (await PostAsync("/admin/actions/settings", await TokenAsync("/admin/settings"), ("displayName", "社内 Portal"))).Headers.Location!.ToString());
        Assert.Contains("社内 Portal", WebUtility.HtmlDecode(await _client.GetStringAsync("/search")));
        HttpResponseMessage invalid = await PostAsync("/admin/actions/settings", await TokenAsync("/admin/settings"), ("displayName", " "));
        Assert.Equal("/admin/settings?error=validation", invalid.Headers.Location!.ToString());
        Assert.Equal("社内 Portal", (await _factory.Services.GetRequiredService<IApplicationSettingsService>().GetAsync()).DisplayName);
        Assert.Equal("/admin/index", (await PostAsync("/admin/actions/test-connection", await TokenAsync("/admin/settings"))).Headers.Location!.ToString());
        Assert.Contains("Could not reach", await _client.GetStringAsync("/admin/index"));
        await PostAsync("/admin/actions/logout", await TokenAsync("/admin/settings"));
        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync("/admin")).StatusCode);
        using HttpClient replay = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task SecurityHeadersAreSetOnEveryResponse()
    {
        foreach (string path in new[] { "/search", "/account/login" })
        {
            HttpResponseMessage response = await _client.GetAsync(path);
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out IEnumerable<string>? xct));
            Assert.Equal("nosniff", xct!.First());
            Assert.True(response.Headers.TryGetValues("X-Frame-Options", out IEnumerable<string>? xfo));
            Assert.Equal("DENY", xfo!.First());
            Assert.True(response.Headers.TryGetValues("Referrer-Policy", out IEnumerable<string>? rp));
            Assert.Equal("strict-origin-when-cross-origin", rp!.First());
            Assert.True(response.Headers.TryGetValues("X-Permitted-Cross-Domain-Policies", out IEnumerable<string>? xpcdp));
            Assert.Equal("none", xpcdp!.First());
        }
    }

    [Fact]
    public async Task OpenRedirect_LoginReturnUrlIsRestricted()
    {
        // A login redirect to an external URL must be rejected.
        HttpResponseMessage response = await _client.GetAsync("/account/login?returnUrl=https%3A%2F%2Fevil.example.com%2F");
        // The page must load (not redirect externally), and the dangerous returnUrl must not appear in the redirect.
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("evil.example.com", html);
    }

    [Fact]
    public async Task CategoryAdminRequiresAuthentication()
    {
        foreach (string path in new[] { "/admin/categories", "/admin/categories/create" })
        {
            HttpResponseMessage response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://localhost/account/login", response.Headers.Location!.ToString());
        }
    }

    private sealed class AdministrationFactory(string directory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
            builder.UseSetting("Storage:DatabasePath", Path.Combine(directory, "database", "lantern.db"));
            builder.UseSetting("Storage:DataProtectionKeysPath", Path.Combine(directory, "keys"));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Meilisearch:Url"] = "http://127.0.0.1:1",
                ["Meilisearch:IndexName"] = "http-test-index"
            }));
        }
    }
}
