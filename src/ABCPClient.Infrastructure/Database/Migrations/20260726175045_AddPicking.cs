using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABCPClient.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPicking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OneCOrderNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Warehouse = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PickingTaskLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PickingTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MatchKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OrderedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Availability = table.Column<int>(type: "INTEGER", nullable: false),
                    IncomingEta = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StockLocation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Barcodes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PositionId = table.Column<long>(type: "INTEGER", nullable: true),
                    PickedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PickedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingTaskLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingTaskLines_PickingTasks_PickingTaskId",
                        column: x => x.PickingTaskId,
                        principalTable: "PickingTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingTaskLines_MatchKey",
                table: "PickingTaskLines",
                column: "MatchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PickingTaskLines_PickingTaskId",
                table: "PickingTaskLines",
                column: "PickingTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingTaskLines_PositionId",
                table: "PickingTaskLines",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingTasks_Number",
                table: "PickingTasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingTasks_OrderNumber",
                table: "PickingTasks",
                column: "OrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PickingTasks_Status",
                table: "PickingTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingTaskLines");

            migrationBuilder.DropTable(
                name: "PickingTasks");
        }
    }
}
