using System.Security.Claims;
using Lantern.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Administration;

public sealed class AdministratorAuthentication(IDbContextFactory<LanternDbContext> factory)
{
    public const string Policy = "Administrator";
    private readonly PasswordHasher<AdministratorAccount> _hasher = new(
        Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AdministratorAccount _dummy = new();
    private string? _dummyHash;

    public async Task ProvisionAsync(string? username, string? password, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.AdministratorAccounts.AnyAsync(ct)) return;
        if (username == null && password == null) return;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 80 || username.Any(char.IsControl) ||
            password == null || password.Length < 14 || password.Length > 128)
            throw new InvalidOperationException("Initial administrator provisioning requires a username and a password of 14–128 characters.");
        var account = new AdministratorAccount { Username = username.Trim() };
        account.PasswordHash = _hasher.HashPassword(account, password);
        db.AdministratorAccounts.Add(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        if (username.Length > 80 || password.Length > 128 || password.Length == 0) return null;
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var account = await db.AdministratorAccounts.SingleOrDefaultAsync(ct);
            _dummyHash ??= _hasher.HashPassword(_dummy, Guid.NewGuid().ToString("N"));
            var result = _hasher.VerifyHashedPassword(account ?? _dummy, account?.PasswordHash ?? _dummyHash, password);
            if (account == null) return null;
            if (account.LockedUntil > DateTimeOffset.UtcNow) return null;
            if (result == PasswordVerificationResult.Failed || !string.Equals(account.Username, username, StringComparison.Ordinal))
            {
                account.FailedAttempts++;
                if (account.FailedAttempts >= 5)
                {
                    account.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                    account.FailedAttempts = 0;
                }
                await db.SaveChangesAsync(ct);
                return null;
            }
            if (result == PasswordVerificationResult.SuccessRehashNeeded) account.PasswordHash = _hasher.HashPassword(account, password);
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            account.SessionStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            return new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, account.Username),
                new Claim(ClaimTypes.Role, Policy), new Claim("session", account.SessionStamp)
            }, CookieAuthenticationDefaults.AuthenticationScheme));
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true || !principal.IsInRole(Policy)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var stamp = await db.AdministratorAccounts.AsNoTracking().Select(a => a.SessionStamp).SingleOrDefaultAsync(ct);
        return stamp != null && stamp == principal.FindFirstValue("session");
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var account = await db.AdministratorAccounts.SingleAsync(ct);
            account.SessionStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
        }
        finally { _gate.Release(); }
    }
}

public sealed class AdministratorCookieEvents(AdministratorAuthentication authentication) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal == null || !await authentication.IsSessionValidAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
