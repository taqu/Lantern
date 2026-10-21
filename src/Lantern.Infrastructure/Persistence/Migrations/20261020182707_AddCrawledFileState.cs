using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantern.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawledFileState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrawledFileStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalPath = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastIndexedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawledFileStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileCrawlerProfiles",
                columns: table => new
                {
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", nullable: false),
                    Recursive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseAllSupportedFormats = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludedExtensions = table.Column<string>(type: "TEXT", nullable: true),
                    ExcludedDirectories = table.Column<string>(type: "TEXT", nullable: true),
                    MaxFileSizeMB = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConcurrentFiles = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileCrawlerProfiles", x => x.CrawlerId);
                    table.CheckConstraint("CK_FileCrawlerProfile_MaxConcurrentFiles", "MaxConcurrentFiles BETWEEN 1 AND 32");
                    table.CheckConstraint("CK_FileCrawlerProfile_MaxFileSizeMB", "MaxFileSizeMB BETWEEN 1 AND 10000");
                    table.CheckConstraint("CK_FileCrawlerProfile_RootPath", "length(trim(RootPath)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawledFileStates_CrawlerId_CanonicalPath",
                table: "CrawledFileStates",
                columns: new[] { "CrawlerId", "CanonicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrawledFileStates_CrawlerId_LastSeenScanId",
                table: "CrawledFileStates",
                columns: new[] { "CrawlerId", "LastSeenScanId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawledFileStates");

            migrationBuilder.DropTable(
                name: "FileCrawlerProfiles");
        }
    }
}
