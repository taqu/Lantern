using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantern.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingMeilisearchTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingMeilisearchTaskUid",
                table: "CrawledFileStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrawledFileStates_SyncStatus",
                table: "CrawledFileStates",
                column: "SyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrawledFileStates_SyncStatus",
                table: "CrawledFileStates");

            migrationBuilder.DropColumn(
                name: "PendingMeilisearchTaskUid",
                table: "CrawledFileStates");
        }
    }
}
