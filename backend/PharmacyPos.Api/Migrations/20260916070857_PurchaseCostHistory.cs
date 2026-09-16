using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseCostHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_cost_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedUnits = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_cost_entries", x => x.Id);
                    table.CheckConstraint("ck_purchase_cost", "\"TotalCost\" >= 0 AND \"ReceivedUnits\" > 0 AND \"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_cost_entries_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_cost_entries_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_cost_entries_ActorId",
                table: "purchase_cost_entries",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_cost_entries_ReceiptId_Revision",
                table: "purchase_cost_entries",
                columns: new[] { "ReceiptId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_cost_entries");
        }
    }
}
