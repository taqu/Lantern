using Lantern.Embedding;
using Lantern.Infrastructure;
using Lantern.Web.Components;
using Lantern.Web.Administration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddLanternInfrastructure(builder.Configuration);
builder.AddLanternAdministration();

var embeddingOptions = builder.Configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new();
if (embeddingOptions.Enabled)
{
    builder.Services.AddLanternEmbedding(builder.Configuration);
}

var app = builder.Build();
await app.InitializeAdministrationAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapAdministrationEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
