using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Jobs;
using Lantern.Crawling.Jobs;
using Lantern.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Administration;

public sealed record CrawlerWithProfile(CrawlerDefinition Definition, FileCrawlerProfile? Profile, WebCrawlerProfile? WebProfile);

public sealed class CrawlerManagementService(
    IDbContextFactory<LanternDbContext> factory,
    CrawlerJobService jobService)
{
    public async Task<List<CrawlerWithProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawlerDefinition> definitions = await db.CrawlerDefinitions.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        List<FileCrawlerProfileRecord> fileRecords = await db.FileCrawlerProfiles.AsNoTracking().ToListAsync(ct);
        List<WebCrawlerProfileRecord> webRecords = await db.WebCrawlerProfiles.AsNoTracking().ToListAsync(ct);
        Dictionary<Guid, FileCrawlerProfileRecord> fileProfileMap = fileRecords.ToDictionary(p => p.CrawlerId);
        Dictionary<Guid, WebCrawlerProfileRecord> webProfileMap = webRecords.ToDictionary(p => p.CrawlerId);
        return definitions.Select(d => new CrawlerWithProfile(
            d,
            fileProfileMap.TryGetValue(d.Id, out FileCrawlerProfileRecord? fp) ? fp.ToDomain() : null,
            webProfileMap.TryGetValue(d.Id, out WebCrawlerProfileRecord? wp) ? wp.ToDomain() : null))
            .ToList();
    }

    public async Task<CrawlerWithProfile?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerDefinition? definition = await db.CrawlerDefinitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (definition == null) return null;
        FileCrawlerProfileRecord? fileRecord = await db.FileCrawlerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CrawlerId == id, ct);
        WebCrawlerProfileRecord? webRecord = await db.WebCrawlerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CrawlerId == id, ct);
        return new CrawlerWithProfile(definition, fileRecord?.ToDomain(), webRecord?.ToDomain());
    }

    public async Task<WebCrawlerProfile?> GetWebProfileAsync(Guid id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        WebCrawlerProfileRecord? record = await db.WebCrawlerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CrawlerId == id, ct);
        return record?.ToDomain();
    }

    public async Task<Guid> CreateAsync(string name, bool enabled, FileCrawlerProfile profile, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        var definition = new CrawlerDefinition { Name = name, CrawlerType = "file", Enabled = enabled };
        profile.CrawlerId = definition.Id;
        db.CrawlerDefinitions.Add(definition);
        db.FileCrawlerProfiles.Add(FileCrawlerProfileRecord.FromDomain(profile));
        await db.SaveChangesAsync(ct);
        return definition.Id;
    }

    public async Task<Guid> CreateWebAsync(string name, bool enabled, WebCrawlerProfile profile, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        var definition = new CrawlerDefinition { Name = name, CrawlerType = "web", Enabled = enabled };
        profile.CrawlerId = definition.Id;
        db.CrawlerDefinitions.Add(definition);
        db.WebCrawlerProfiles.Add(WebCrawlerProfileRecord.FromDomain(profile));
        await db.SaveChangesAsync(ct);
        return definition.Id;
    }

    public async Task UpdateAsync(Guid id, string name, bool enabled, FileCrawlerProfile profile, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerDefinition definition = await db.CrawlerDefinitions.FirstAsync(c => c.Id == id, ct);
        definition.Name = name;
        definition.Enabled = enabled;
        definition.UpdatedAt = DateTimeOffset.UtcNow;
        FileCrawlerProfileRecord? existing = await db.FileCrawlerProfiles.FirstOrDefaultAsync(p => p.CrawlerId == id, ct);
        if (existing != null)
        {
            existing.RootPath = profile.RootPath;
            existing.Recursive = profile.Recursive;
            existing.UseAllSupportedFormats = profile.UseAllSupportedFormats;
            existing.IncludedExtensions = profile.IncludedExtensions.Count > 0 ? string.Join(",", profile.IncludedExtensions) : null;
            existing.ExcludedDirectories = profile.ExcludedDirectories.Count > 0 ? string.Join(",", profile.ExcludedDirectories) : null;
            existing.MaxFileSizeMB = profile.MaxFileSizeMB;
            existing.MaxConcurrentFiles = profile.MaxConcurrentFiles;
        }
        else
        {
            profile.CrawlerId = id;
            db.FileCrawlerProfiles.Add(FileCrawlerProfileRecord.FromDomain(profile));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateWebAsync(Guid id, string name, bool enabled, WebCrawlerProfile profile, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerDefinition definition = await db.CrawlerDefinitions.FirstAsync(c => c.Id == id, ct);
        definition.Name = name;
        definition.Enabled = enabled;
        definition.UpdatedAt = DateTimeOffset.UtcNow;
        WebCrawlerProfileRecord? existing = await db.WebCrawlerProfiles.FirstOrDefaultAsync(p => p.CrawlerId == id, ct);
        if (existing != null)
        {
            existing.StartUrls = WebCrawlerProfileRecord.FromDomain(profile).StartUrls;
            existing.AllowedHosts = WebCrawlerProfileRecord.FromDomain(profile).AllowedHosts;
            existing.AllowedPathPrefixes = WebCrawlerProfileRecord.FromDomain(profile).AllowedPathPrefixes;
            existing.ExcludeUrlPatterns = WebCrawlerProfileRecord.FromDomain(profile).ExcludeUrlPatterns;
            existing.MaximumDepth = profile.MaximumDepth;
            existing.MaximumPages = profile.MaximumPages;
            existing.RequestDelayMilliseconds = profile.RequestDelayMilliseconds;
            existing.MaximumConcurrentRequests = profile.MaximumConcurrentRequests;
            existing.MaximumResponseSizeMB = profile.MaximumResponseSizeMB;
            existing.FollowRedirects = profile.FollowRedirects;
            existing.RespectRobotsTxt = profile.RespectRobotsTxt;
            existing.IndexLinkedDocuments = profile.IndexLinkedDocuments;
        }
        else
        {
            profile.CrawlerId = id;
            db.WebCrawlerProfiles.Add(WebCrawlerProfileRecord.FromDomain(profile));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerDefinition definition = await db.CrawlerDefinitions.FirstAsync(c => c.Id == id, ct);
        definition.Enabled = enabled;
        definition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // Deletes the crawler definition and its profile and crawl state. Job history is preserved.
    // Documents already indexed in Meilisearch are NOT deleted — they remain searchable.
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.FileCrawlerProfiles.Where(p => p.CrawlerId == id).ExecuteDeleteAsync(ct);
        await db.WebCrawlerProfiles.Where(p => p.CrawlerId == id).ExecuteDeleteAsync(ct);
        await db.CrawledFileStates.Where(s => s.CrawlerId == id).ExecuteDeleteAsync(ct);
        await db.CrawledUrlStates.Where(s => s.CrawlerId == id).ExecuteDeleteAsync(ct);
        await db.CrawlerDefinitions.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        await jobService.DeleteScheduleAsync(id, ct);
    }

    // Submits a persistent manual job. Returns the submission result including the job ID.
    public async Task<JobSubmissionResult> TryStartAsync(Guid id, CancellationToken ct = default)
    {
        CrawlerWithProfile? item = await GetAsync(id, ct);
        if (item == null) return new JobSubmissionResult(JobSubmissionStatus.NotFound, null);
        if (!item.Definition.Enabled) return new JobSubmissionResult(JobSubmissionStatus.Disabled, null);

        if (item.Definition.CrawlerType == "web")
        {
            if (item.WebProfile == null) return new JobSubmissionResult(JobSubmissionStatus.InvalidConfiguration, null);
            WebCrawlerConfiguration webConfig = item.WebProfile.ToConfiguration(item.Definition.Name);
            return await jobService.SubmitManualJobAsync(id, item.Definition.Name, webConfig, ct);
        }
        else
        {
            if (item.Profile == null) return new JobSubmissionResult(JobSubmissionStatus.InvalidConfiguration, null);
            FileCrawlerConfiguration config = item.Profile.ToConfiguration(item.Definition.Name);
            return await jobService.SubmitManualJobAsync(id, item.Definition.Name, config, ct);
        }
    }
}
