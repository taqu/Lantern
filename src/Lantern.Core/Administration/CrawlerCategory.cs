namespace Lantern.Core.Administration;

public sealed class CrawlerCategory
{
    public int Id { get; set; }
    public Guid CrawlerId { get; set; }
    public int CategoryId { get; set; }
}
