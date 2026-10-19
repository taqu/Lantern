using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.Sqlite;

namespace Lantern.Infrastructure.Persistence;

// Design-time tooling only; the running application uses IDbContextFactory from DI.
public sealed class LanternDbContextFactory : IDesignTimeDbContextFactory<LanternDbContext>
{
    public LanternDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("LANTERN_MIGRATION_DATABASE")
            ?? Path.Combine(Path.GetTempPath(), "lantern-design.sqlite");
        var connection = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path) };
        return new(new DbContextOptionsBuilder<LanternDbContext>().UseSqlite(connection.ToString()).Options);
    }
}
