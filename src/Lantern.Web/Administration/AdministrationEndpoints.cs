using System.ComponentModel.DataAnnotations;
using Lantern.Core.Administration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Lantern.Web.Administration;

public static class AdministrationEndpoints
{
    public static void MapAdministrationEndpoints(this WebApplication app)
    {
        app.MapPost("/account/sign-in", async (HttpContext context, IAntiforgery antiforgery, AdministratorAuthentication authentication) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest();
            IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            System.Security.Claims.ClaimsPrincipal? principal = await authentication.ValidateAsync(form["username"].ToString(), form["password"].ToString(), context.RequestAborted);
            if (principal == null) return Results.LocalRedirect("/account/login?invalid=true");
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = false });
            return Results.LocalRedirect("/admin");
        }).AllowAnonymous().RequireRateLimiting("login");

        RouteGroupBuilder admin = app.MapGroup("/admin/actions").RequireAuthorization(AdministratorAuthentication.Policy);
        admin.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, AdministratorAuthentication authentication) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest();
            await authentication.RevokeAsync(context.RequestAborted);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/account/login");
        });
        admin.MapPost("/settings", async (HttpContext context, IAntiforgery antiforgery, AdministrationService service, ILogger<AdministrationService> logger) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest();
            IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                await service.SaveSettingsAsync(new ApplicationSetting { DisplayName = form["displayName"].ToString() }, context.RequestAborted);
                return Results.LocalRedirect("/admin/settings?saved=true");
            }
            catch (ValidationException) { return Results.LocalRedirect("/admin/settings?error=validation"); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                logger.LogError("An administrator settings update could not be persisted");
                return Results.LocalRedirect("/admin/settings?error=save");
            }
        });
        admin.MapPost("/test-connection", async (HttpContext context, IAntiforgery antiforgery, AdministrationService service) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest();
            await service.GetHealthAsync(true, context.RequestAborted);
            return Results.LocalRedirect("/admin/index");
        });
    }

    private static async Task<bool> ValidFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.HasFormContentType || context.Request.ContentLength > 16384) return false;
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
}
