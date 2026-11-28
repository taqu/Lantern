namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerCategoryRecord
{
    public int Id { get; set; }
    public Guid CrawlerId { get; set; }
    public int CategoryId { get; set; }
}
