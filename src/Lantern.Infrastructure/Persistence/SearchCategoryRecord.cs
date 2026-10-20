namespace Lantern.Infrastructure.Persistence;

public sealed class SearchCategoryRecord
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Lantern.Core.Administration.SearchCategory ToDomain() => new()
    {
        Id = Id, Key = Key, Name = Name, Description = Description,
        SortOrder = SortOrder, Enabled = Enabled, CreatedAt = CreatedAt, UpdatedAt = UpdatedAt
    };

    public static SearchCategoryRecord FromDomain(Lantern.Core.Administration.SearchCategory d) => new()
    {
        Id = d.Id, Key = d.Key, Name = d.Name, Description = d.Description,
        SortOrder = d.SortOrder, Enabled = d.Enabled, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt
    };
}
