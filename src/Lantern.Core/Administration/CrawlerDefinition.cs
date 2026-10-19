using System.ComponentModel.DataAnnotations;

namespace Lantern.Core.Administration;

public sealed class CrawlerDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string CrawlerType { get; set; } = "file";
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl))
            throw new ValidationException("A crawler requires a stable ID and a name of 1–120 characters.");
        if (CrawlerType is not ("file" or "web")) throw new ValidationException("Crawler type must be file or web.");
        Name = Name.Trim();
        CreatedAt = CreatedAt.ToUniversalTime();
        UpdatedAt = UpdatedAt.ToUniversalTime();
    }
}
