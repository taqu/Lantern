using Lantern.Core.Administration;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class LanternDbContext(DbContextOptions<LanternDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<CrawlerDefinition> CrawlerDefinitions => Set<CrawlerDefinition>();
    public DbSet<AdministratorAccount> AdministratorAccounts => Set<AdministratorAccount>();
    public DbSet<CrawledFileState> CrawledFileStates => Set<CrawledFileState>();
    public DbSet<FileCrawlerProfileRecord> FileCrawlerProfiles => Set<FileCrawlerProfileRecord>();
    public DbSet<CrawlerJobRecord> CrawlerJobs => Set<CrawlerJobRecord>();
    public DbSet<CrawlerScheduleRecord> CrawlerSchedules => Set<CrawlerScheduleRecord>();
    public DbSet<CrawlerJobEventRecord> CrawlerJobEvents => Set<CrawlerJobEventRecord>();
    public DbSet<WebCrawlerProfileRecord> WebCrawlerProfiles => Set<WebCrawlerProfileRecord>();
    public DbSet<CrawledUrlState> CrawledUrlStates => Set<CrawledUrlState>();
    public DbSet<SearchCategoryRecord> SearchCategories => Set<SearchCategoryRecord>();
    public DbSet<CrawlerCategoryRecord> CrawlerCategories => Set<CrawlerCategoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ApplicationSetting> settings = modelBuilder.Entity<ApplicationSetting>();
        settings.ToTable("ApplicationSettings", t =>
        {
            t.HasCheckConstraint("CK_Settings_Singleton", "Id = 1");
            t.HasCheckConstraint("CK_Settings_Name", "length(trim(DisplayName)) BETWEEN 1 AND 80");
        });
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Id).ValueGeneratedNever();
        settings.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawlerDefinition> crawler = modelBuilder.Entity<CrawlerDefinition>();
        crawler.ToTable("CrawlerDefinitions", t =>
        {
            t.HasCheckConstraint("CK_Crawler_Type", "CrawlerType IN ('file', 'web')");
            t.HasCheckConstraint("CK_Crawler_Name", "length(trim(Name)) BETWEEN 1 AND 120");
        });
        crawler.HasKey(x => x.Id);
        crawler.Property(x => x.Name).IsRequired().HasMaxLength(120);
        crawler.Property(x => x.CrawlerType).IsRequired().HasMaxLength(4);
        crawler.HasIndex(x => x.Name);

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AdministratorAccount> admin = modelBuilder.Entity<AdministratorAccount>();
        admin.ToTable("AdministratorAccounts", t => t.HasCheckConstraint("CK_Admin_Singleton", "Id = 1"));
        admin.HasKey(x => x.Id);
        admin.Property(x => x.Id).ValueGeneratedNever();
        admin.Property(x => x.Username).IsRequired().HasMaxLength(80);
        admin.Property(x => x.PasswordHash).IsRequired();
        admin.Property(x => x.SessionStamp).IsRequired();
        admin.HasIndex(x => x.Username).IsUnique();

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawledFileState> fileState = modelBuilder.Entity<CrawledFileState>();
        fileState.ToTable("CrawledFileStates");
        fileState.HasKey(x => x.Id);
        fileState.Property(x => x.DocumentId).IsRequired();
        fileState.Property(x => x.CanonicalPath).IsRequired();
        fileState.Property(x => x.SyncStatus).IsRequired().HasMaxLength(20);
        fileState.HasIndex(x => new { x.CrawlerId, x.CanonicalPath }).IsUnique();
        fileState.HasIndex(x => new { x.CrawlerId, x.LastSeenScanId });

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<FileCrawlerProfileRecord> profile = modelBuilder.Entity<FileCrawlerProfileRecord>();
        profile.ToTable("FileCrawlerProfiles", t =>
        {
            t.HasCheckConstraint("CK_FileCrawlerProfile_MaxFileSizeMB", "MaxFileSizeMB BETWEEN 1 AND 10000");
            t.HasCheckConstraint("CK_FileCrawlerProfile_MaxConcurrentFiles", "MaxConcurrentFiles BETWEEN 1 AND 32");
            t.HasCheckConstraint("CK_FileCrawlerProfile_RootPath", "length(trim(RootPath)) > 0");
        });
        profile.HasKey(x => x.CrawlerId);
        profile.Property(x => x.CrawlerId).ValueGeneratedNever();
        profile.Property(x => x.RootPath).IsRequired();

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawlerJobRecord> job = modelBuilder.Entity<CrawlerJobRecord>();
        job.ToTable("CrawlerJobs", t =>
        {
            t.HasCheckConstraint("CK_Job_Status", "Status IN ('Pending','Running','Completed','Failed','Canceled')");
            t.HasCheckConstraint("CK_Job_Trigger", "Trigger IN ('Manual','Scheduled','Retry')");
            t.HasCheckConstraint("CK_Job_AttemptNumber", "AttemptNumber >= 1");
        });
        job.HasKey(x => x.Id);
        job.Property(x => x.Id).ValueGeneratedNever();
        job.Property(x => x.CrawlerName).IsRequired().HasMaxLength(120);
        job.Property(x => x.Trigger).IsRequired().HasMaxLength(16);
        job.Property(x => x.Status).IsRequired().HasMaxLength(16);
        job.HasIndex(x => x.Status);
        job.HasIndex(x => x.CrawlerId);
        job.HasIndex(x => x.CreatedAt);
        job.HasIndex(x => new { x.CrawlerId, x.Status });

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawlerScheduleRecord> schedule = modelBuilder.Entity<CrawlerScheduleRecord>();
        schedule.ToTable("CrawlerSchedules", t =>
            t.HasCheckConstraint("CK_Schedule_Type", "Type IN ('Disabled','Hourly','Daily','Weekly')"));
        schedule.HasKey(x => x.CrawlerId);
        schedule.Property(x => x.CrawlerId).ValueGeneratedNever();
        schedule.Property(x => x.Type).IsRequired().HasMaxLength(16);
        schedule.HasIndex(x => x.NextRunAt);

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawlerJobEventRecord> evt = modelBuilder.Entity<CrawlerJobEventRecord>();
        evt.ToTable("CrawlerJobEvents");
        evt.HasKey(x => x.Id);
        evt.Property(x => x.EventType).IsRequired().HasMaxLength(32);
        evt.HasIndex(x => x.JobId);
        evt.HasIndex(x => x.OccurredAt);

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WebCrawlerProfileRecord> webProfile = modelBuilder.Entity<WebCrawlerProfileRecord>();
        webProfile.ToTable("WebCrawlerProfiles", t =>
        {
            t.HasCheckConstraint("CK_WebProfile_MaximumDepth", "MaximumDepth BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_WebProfile_MaximumPages", "MaximumPages BETWEEN 1 AND 1000000");
            t.HasCheckConstraint("CK_WebProfile_MaximumConcurrentRequests", "MaximumConcurrentRequests BETWEEN 1 AND 16");
        });
        webProfile.HasKey(x => x.CrawlerId);
        webProfile.Property(x => x.CrawlerId).ValueGeneratedNever();
        webProfile.Property(x => x.StartUrls).IsRequired();
        webProfile.Property(x => x.AllowedHosts).IsRequired();

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawledUrlState> urlState = modelBuilder.Entity<CrawledUrlState>();
        urlState.ToTable("CrawledUrlStates");
        urlState.HasKey(x => x.Id);
        urlState.Property(x => x.CanonicalUrl).IsRequired().HasMaxLength(2048);
        urlState.Property(x => x.SyncStatus).IsRequired().HasMaxLength(20);
        urlState.HasIndex(x => new { x.CrawlerId, x.CanonicalUrl }).IsUnique();
        urlState.HasIndex(x => new { x.CrawlerId, x.LastSeenScanId });

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SearchCategoryRecord> category = modelBuilder.Entity<SearchCategoryRecord>();
        category.ToTable("SearchCategories", t =>
        {
            t.HasCheckConstraint("CK_Category_Key", "length(trim(Key)) BETWEEN 1 AND 60");
            t.HasCheckConstraint("CK_Category_Name", "length(trim(Name)) BETWEEN 1 AND 80");
        });
        category.HasKey(x => x.Id);
        category.Property(x => x.Key).IsRequired().HasMaxLength(60);
        category.Property(x => x.Name).IsRequired().HasMaxLength(80);
        category.Property(x => x.Description).HasMaxLength(500);
        category.HasIndex(x => x.Key).IsUnique();
        category.HasIndex(x => x.SortOrder);

        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CrawlerCategoryRecord> crawlerCategory = modelBuilder.Entity<CrawlerCategoryRecord>();
        crawlerCategory.ToTable("CrawlerCategories");
        crawlerCategory.HasKey(x => x.Id);
        crawlerCategory.HasIndex(x => new { x.CrawlerId, x.CategoryId }).IsUnique();
        crawlerCategory.HasIndex(x => x.CrawlerId);
        crawlerCategory.HasIndex(x => x.CategoryId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ValidateChanges();
        return base.SaveChanges();
    }

    private static void ValidateFileCrawlerProfile(FileCrawlerProfileRecord fp)
    {
        if (string.IsNullOrWhiteSpace(fp.RootPath))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Root path is required.");
        if (fp.MaxFileSizeMB is < 1 or > 10000)
            throw new System.ComponentModel.DataAnnotations.ValidationException("MaxFileSizeMB must be between 1 and 10000.");
        if (fp.MaxConcurrentFiles is < 1 or > 32)
            throw new System.ComponentModel.DataAnnotations.ValidationException("MaxConcurrentFiles must be between 1 and 32.");
    }

    private void ValidateChanges()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry? entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is ApplicationSetting settings) settings.Validate();
            if (entry.Entity is CrawlerDefinition crawler) crawler.Validate();
            if (entry.Entity is FileCrawlerProfileRecord fp) ValidateFileCrawlerProfile(fp);
            if (entry.Entity is SearchCategoryRecord cat) cat.ToDomain().Validate();
        }
    }
}
