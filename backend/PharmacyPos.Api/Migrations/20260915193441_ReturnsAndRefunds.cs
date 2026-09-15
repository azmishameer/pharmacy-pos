using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sale_returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_returns", x => x.Id);
                    table.UniqueConstraint("AK_sale_returns_Id_SaleId", x => new { x.Id, x.SaleId });
                    table.CheckConstraint("ck_return_amount", "\"Amount\" >= 0");
                    table.ForeignKey(
                        name: "FK_sale_returns_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_returns_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "returned_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineIndex = table.Column<int>(type: "integer", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Packs = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Refund = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true),
                    ReviewerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VisibleUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_returned_items", x => x.Id);
                    table.CheckConstraint("ck_returned_quantity", "\"Quantity\" > 0 AND \"Packs\" > 0 AND \"LineIndex\" >= 0 AND \"Refund\" >= 0");
                    table.CheckConstraint("ck_returned_status", "\"Status\" IN ('Held','Restocked','Disposed')");
                    table.ForeignKey(
                        name: "FK_returned_items_AspNetUsers_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_returned_items_sale_returns_ReturnId_SaleId",
                        columns: x => new { x.ReturnId, x.SaleId },
                        principalTable: "sale_returns",
                        principalColumns: new[] { "Id", "SaleId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_returned_items_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_ReceiptId_Status",
                table: "returned_items",
                columns: new[] { "ReceiptId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_ReturnId_SaleId",
                table: "returned_items",
                columns: new[] { "ReturnId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_ReviewedBy",
                table: "returned_items",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_SaleId_LineIndex",
                table: "returned_items",
                columns: new[] { "SaleId", "LineIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_returns_ActorId",
                table: "sale_returns",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_returns_At",
                table: "sale_returns",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_sale_returns_SaleId",
                table: "sale_returns",
                column: "SaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "returned_items");

            migrationBuilder.DropTable(
                name: "sale_returns");
        }
    }
}
