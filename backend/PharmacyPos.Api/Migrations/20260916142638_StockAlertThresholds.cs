using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class StockAlertThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_threshold_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousMinimum = table.Column<long>(type: "bigint", nullable: true),
                    Minimum = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_threshold_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_threshold_events_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_threshold_events_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_thresholds",
                columns: table => new
                {
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Minimum = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_thresholds", x => x.MedicineId);
                    table.CheckConstraint("ck_stock_threshold", "\"Minimum\" IS NULL OR \"Minimum\" >= 0");
                    table.ForeignKey(
                        name: "FK_stock_thresholds_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_threshold_events_ActorId",
                table: "stock_threshold_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_threshold_events_MedicineId_At",
                table: "stock_threshold_events",
                columns: new[] { "MedicineId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_threshold_events");

            migrationBuilder.DropTable(
                name: "stock_thresholds");
        }
    }
}
