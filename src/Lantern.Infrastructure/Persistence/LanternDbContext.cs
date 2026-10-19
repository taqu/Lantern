using Lantern.Core.Administration;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class LanternDbContext(DbContextOptions<LanternDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<CrawlerDefinition> CrawlerDefinitions => Set<CrawlerDefinition>();
    public DbSet<AdministratorAccount> AdministratorAccounts => Set<AdministratorAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<ApplicationSetting>();
        settings.ToTable("ApplicationSettings", t =>
        {
            t.HasCheckConstraint("CK_Settings_Singleton", "Id = 1");
            t.HasCheckConstraint("CK_Settings_Name", "length(trim(DisplayName)) BETWEEN 1 AND 80");
        });
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Id).ValueGeneratedNever();
        settings.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);

        var crawler = modelBuilder.Entity<CrawlerDefinition>();
        crawler.ToTable("CrawlerDefinitions", t =>
        {
            t.HasCheckConstraint("CK_Crawler_Type", "CrawlerType IN ('file', 'web')");
            t.HasCheckConstraint("CK_Crawler_Name", "length(trim(Name)) BETWEEN 1 AND 120");
        });
        crawler.HasKey(x => x.Id);
        crawler.Property(x => x.Name).IsRequired().HasMaxLength(120);
        crawler.Property(x => x.CrawlerType).IsRequired().HasMaxLength(4);
        crawler.HasIndex(x => x.Name);

        var admin = modelBuilder.Entity<AdministratorAccount>();
        admin.ToTable("AdministratorAccounts", t => t.HasCheckConstraint("CK_Admin_Singleton", "Id = 1"));
        admin.HasKey(x => x.Id);
        admin.Property(x => x.Id).ValueGeneratedNever();
        admin.Property(x => x.Username).IsRequired().HasMaxLength(80);
        admin.Property(x => x.PasswordHash).IsRequired();
        admin.Property(x => x.SessionStamp).IsRequired();
        admin.HasIndex(x => x.Username).IsUnique();
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

    private void ValidateChanges()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is ApplicationSetting settings) settings.Validate();
            if (entry.Entity is CrawlerDefinition crawler) crawler.Validate();
        }
    }
}
