using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ABCPClient.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InternalNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClientOrderNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UserFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UserEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UserMobile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    ManagerId = table.Column<int>(type: "INTEGER", nullable: true),
                    PositionsQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Sum = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Debt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ShipmentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DeliveryOffice = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DeliveryType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DeliveryCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PaymentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DominantStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    DominantStatusName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    HasMixedStatuses = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatuses",
                columns: table => new
                {
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Notify = table.Column<bool>(type: "INTEGER", nullable: false),
                    Paid = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartDelivery = table.Column<bool>(type: "INTEGER", nullable: false),
                    Delivery = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlacingOrder = table.Column<bool>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatuses", x => x.StatusCode);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SyncLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WindowFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OrdersFetched = table.Column<int>(type: "INTEGER", nullable: false),
                    OrdersCreated = table.Column<int>(type: "INTEGER", nullable: false),
                    OrdersUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusChanges = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PositionId = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BrandFix = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NumberFix = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    QuantityFinal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    PriceIn = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PriceOut = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PriceInSiteCurrency = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    CurrencyInId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyOutId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeadlineHours = table.Column<int>(type: "INTEGER", nullable: true),
                    DeadlineMaxHours = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    StatusChangeDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelRequest = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DistributorId = table.Column<int>(type: "INTEGER", nullable: true),
                    DistributorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DistributorType = table.Column<int>(type: "INTEGER", nullable: false),
                    DistributorOrderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RouteId = table.Column<int>(type: "INTEGER", nullable: true),
                    SupplierCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CommentAnswer = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Weight = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItemStatusHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ManagerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ManagerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItemStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItemStatusHistory_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_Brand_Number",
                table: "OrderItems",
                columns: new[] { "Brand", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_PositionId",
                table: "OrderItems",
                column: "PositionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_StatusCode",
                table: "OrderItems",
                column: "StatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemStatusHistory_OrderItemId_StatusCode_ChangedAt",
                table: "OrderItemStatusHistory",
                columns: new[] { "OrderItemId", "StatusCode", "ChangedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Date",
                table: "Orders",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DateUpdated",
                table: "Orders",
                column: "DateUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DominantStatusCode",
                table: "Orders",
                column: "DominantStatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_InternalNumber",
                table: "Orders",
                column: "InternalNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncLog_StartedAt",
                table: "SyncLog",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItemStatusHistory");

            migrationBuilder.DropTable(
                name: "OrderStatuses");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SyncLog");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
