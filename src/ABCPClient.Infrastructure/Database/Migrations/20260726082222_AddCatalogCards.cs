using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABCPClient.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcodes",
                table: "ArticleCards",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ArticleCards",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Barcodes",
                table: "ArticleCards");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ArticleCards");
        }
    }
}
