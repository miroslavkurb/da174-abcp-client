using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABCPClient.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArticleCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NumberFix = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ImageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ImagesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    NotFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCards_Brand_Number",
                table: "ArticleCards",
                columns: new[] { "Brand", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleCards");
        }
    }
}
