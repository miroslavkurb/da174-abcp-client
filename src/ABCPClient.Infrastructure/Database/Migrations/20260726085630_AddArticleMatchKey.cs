using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABCPClient.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleMatchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchKey",
                table: "ArticleCards",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCards_MatchKey",
                table: "ArticleCards",
                column: "MatchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArticleCards_MatchKey",
                table: "ArticleCards");

            migrationBuilder.DropColumn(
                name: "MatchKey",
                table: "ArticleCards");
        }
    }
}
