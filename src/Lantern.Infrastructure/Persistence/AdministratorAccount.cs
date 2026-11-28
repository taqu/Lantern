namespace Lantern.Infrastructure.Persistence;

// Never use this persistence entity as a UI or API response model.
public sealed class AdministratorAccount
{
    public int Id { get; set; } = 1;
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SessionStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
