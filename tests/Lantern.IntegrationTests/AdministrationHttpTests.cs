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
        var html = await _client.GetStringAsync(path);
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
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
        foreach (var path in new[] { "/admin", "/admin/crawlers", "/admin/jobs", "/admin/index", "/admin/settings", "/admin/system" })
        {
            var response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://localhost/account/login", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/admin/actions/settings", "/admin/actions/logout", "/admin/actions/test-connection" })
        {
            var response = await _client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/search")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ServiceProvider.GetRequiredService<AdministrationService>()
            .SaveSettingsAsync(new ApplicationSetting { DisplayName = "unauthorized" }));
    }

    [Fact]
    public async Task SettingsAccountAndAuthenticationKeysSurviveHostRestart()
    {
        var login = await LoginAsync();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Lantern.Admin="));
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
        var token = await TokenAsync("/account/login");
        HttpResponseMessage? response = null;
        for (var i = 0; i < 10; i++)
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
        using var scope = _factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var ordinaryUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "ordinary") }, "test"));
        Assert.False((await authorization.AuthorizeAsync(ordinaryUser, null, AdministratorAuthentication.Policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(), null, AdministratorAuthentication.Policy)).Succeeded);
    }

    [Fact]
    public async Task LoginSettingsAntiforgeryLogoutAndCookieReplay()
    {
        Assert.Equal("/account/login?invalid=true", (await LoginAsync("wrong")).Headers.Location!.ToString());
        var login = await LoginAsync();
        Assert.Equal("/admin", login.Headers.Location!.ToString());
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Lantern.Admin="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { "/admin", "/admin/crawlers", "/admin/jobs", "/admin/index", "/admin/settings", "/admin/system" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        var settingsHtml = await _client.GetStringAsync("/admin/settings");
        Assert.DoesNotContain("PasswordHash", settingsHtml);
        Assert.DoesNotContain("http-test-long-password", settingsHtml);
        var key = System.Environment.GetEnvironmentVariable("MEILI_API_KEY");
        Assert.False(!string.IsNullOrEmpty(key) && settingsHtml.Contains(key, StringComparison.Ordinal));
        var noCsrf = await _client.PostAsync("/admin/actions/settings", new FormUrlEncodedContent(new Dictionary<string, string> { ["displayName"] = "CSRF" }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("/admin/settings?saved=true", (await PostAsync("/admin/actions/settings", await TokenAsync("/admin/settings"), ("displayName", "社内 Portal"))).Headers.Location!.ToString());
        Assert.Contains("社内 Portal", WebUtility.HtmlDecode(await _client.GetStringAsync("/search")));
        var invalid = await PostAsync("/admin/actions/settings", await TokenAsync("/admin/settings"), ("displayName", " "));
        Assert.Equal("/admin/settings?error=validation", invalid.Headers.Location!.ToString());
        Assert.Equal("社内 Portal", (await _factory.Services.GetRequiredService<IApplicationSettingsService>().GetAsync()).DisplayName);
        Assert.Equal("/admin/index", (await PostAsync("/admin/actions/test-connection", await TokenAsync("/admin/settings"))).Headers.Location!.ToString());
        Assert.Contains("Could not reach", await _client.GetStringAsync("/admin/index"));
        await PostAsync("/admin/actions/logout", await TokenAsync("/admin/settings"));
        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync("/admin")).StatusCode);
        using var replay = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/admin/settings")).StatusCode);
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
                ["Meilisearch:Url"] = "http://127.0.0.1:1", ["Meilisearch:IndexName"] = "http-test-index"
            }));
        }
    }
}
