using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class CashCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    OperatorId = table.Column<string>(type: "text", nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_AspNetUsers_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Change = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payments", x => x.Id);
                    table.CheckConstraint("ck_cash_payment", "\"Amount\" >= 0 AND \"Tendered\" >= \"Amount\" AND \"Change\" = \"Tendered\" - \"Amount\"");
                    table.ForeignKey(
                        name: "FK_sale_payments_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_stock_movements",
                columns: table => new
                {
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_stock_movements", x => new { x.SaleId, x.ReceiptId });
                    table.CheckConstraint("ck_sale_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_stock_movements_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_stock_movements_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_SaleId",
                table: "sale_payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_stock_movements_ReceiptId",
                table: "sale_stock_movements",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_Number",
                table: "sales",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_OperatorId_CompletedAt",
                table: "sales",
                columns: new[] { "OperatorId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_payments");

            migrationBuilder.DropTable(
                name: "sale_stock_movements");

            migrationBuilder.DropTable(
                name: "sales");
        }
    }
}
