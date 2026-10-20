namespace Lantern.Core.Administration;

public sealed class SearchCategory
{
    public int Id { get; set; }
    public string Key { get; set; } = "";       // URL-friendly, unique slug
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || Key.Length > 60)
            throw new System.ComponentModel.DataAnnotations.ValidationException("Category key is required and must be 1-60 characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Key, @"^[a-z0-9][a-z0-9\-]*$"))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Category key must contain only lowercase letters, digits, and hyphens.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80)
            throw new System.ComponentModel.DataAnnotations.ValidationException("Category name is required and must be 1-80 characters.");
        if (Description?.Length > 500)
            throw new System.ComponentModel.DataAnnotations.ValidationException("Description must be 500 characters or less.");
    }
}
