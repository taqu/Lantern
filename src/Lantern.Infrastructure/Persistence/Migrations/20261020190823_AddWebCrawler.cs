using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantern.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebCrawler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrawledUrlStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: true),
                    ETag = table.Column<string>(type: "TEXT", nullable: true),
                    LastModified = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastIndexedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastHttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawledUrlStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebCrawlerProfiles",
                columns: table => new
                {
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartUrls = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedHosts = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedPathPrefixes = table.Column<string>(type: "TEXT", nullable: true),
                    ExcludeUrlPatterns = table.Column<string>(type: "TEXT", nullable: true),
                    MaximumDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumPages = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestDelayMilliseconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumConcurrentRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumResponseSizeMB = table.Column<int>(type: "INTEGER", nullable: false),
                    FollowRedirects = table.Column<bool>(type: "INTEGER", nullable: false),
                    RespectRobotsTxt = table.Column<bool>(type: "INTEGER", nullable: false),
                    IndexLinkedDocuments = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebCrawlerProfiles", x => x.CrawlerId);
                    table.CheckConstraint("CK_WebProfile_MaximumConcurrentRequests", "MaximumConcurrentRequests BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_WebProfile_MaximumDepth", "MaximumDepth BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_WebProfile_MaximumPages", "MaximumPages BETWEEN 1 AND 1000000");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawledUrlStates_CrawlerId_CanonicalUrl",
                table: "CrawledUrlStates",
                columns: new[] { "CrawlerId", "CanonicalUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrawledUrlStates_CrawlerId_LastSeenScanId",
                table: "CrawledUrlStates",
                columns: new[] { "CrawlerId", "LastSeenScanId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawledUrlStates");

            migrationBuilder.DropTable(
                name: "WebCrawlerProfiles");
        }
    }
}
