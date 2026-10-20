using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantern.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlerJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrawlerJobEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlerJobEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawlerJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrawlerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CancellationRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetryOfJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DiscoveredFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessedFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexedFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    UnchangedFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    SkippedFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    FailedFiles = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedFiles = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlerJobs", x => x.Id);
                    table.CheckConstraint("CK_Job_AttemptNumber", "AttemptNumber >= 1");
                    table.CheckConstraint("CK_Job_Status", "Status IN ('Pending','Running','Completed','Failed','Canceled')");
                    table.CheckConstraint("CK_Job_Trigger", "Trigger IN ('Manual','Scheduled','Retry')");
                });

            migrationBuilder.CreateTable(
                name: "CrawlerSchedules",
                columns: table => new
                {
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TimeOfDay = table.Column<string>(type: "TEXT", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: true),
                    DaysOfWeek = table.Column<string>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlerSchedules", x => x.CrawlerId);
                    table.CheckConstraint("CK_Schedule_Type", "Type IN ('Disabled','Hourly','Daily','Weekly')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobEvents_JobId",
                table: "CrawlerJobEvents",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobEvents_OccurredAt",
                table: "CrawlerJobEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobs_CrawlerId",
                table: "CrawlerJobs",
                column: "CrawlerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobs_CrawlerId_Status",
                table: "CrawlerJobs",
                columns: new[] { "CrawlerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobs_CreatedAt",
                table: "CrawlerJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerJobs_Status",
                table: "CrawlerJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerSchedules_NextRunAt",
                table: "CrawlerSchedules",
                column: "NextRunAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawlerJobEvents");

            migrationBuilder.DropTable(
                name: "CrawlerJobs");

            migrationBuilder.DropTable(
                name: "CrawlerSchedules");
        }
    }
}
