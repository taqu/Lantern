using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantern.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchCategories", x => x.Id);
                    table.CheckConstraint("CK_Category_Key", "length(trim(Key)) BETWEEN 1 AND 60");
                    table.CheckConstraint("CK_Category_Name", "length(trim(Name)) BETWEEN 1 AND 80");
                });

            migrationBuilder.CreateTable(
                name: "CrawlerCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CrawlerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlerCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchCategories_Key",
                table: "SearchCategories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchCategories_SortOrder",
                table: "SearchCategories",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerCategories_CrawlerId_CategoryId",
                table: "CrawlerCategories",
                columns: new[] { "CrawlerId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerCategories_CrawlerId",
                table: "CrawlerCategories",
                column: "CrawlerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlerCategories_CategoryId",
                table: "CrawlerCategories",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CrawlerCategories");
            migrationBuilder.DropTable(name: "SearchCategories");
        }
    }
}
