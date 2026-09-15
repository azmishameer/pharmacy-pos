using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class CartCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charge_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AllMedicines = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StoppedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charge_rules", x => x.Id);
                    table.CheckConstraint("ck_charge_kind", "\"Kind\" IN ('Percentage', 'PerUnit', 'PerMedicine')");
                    table.CheckConstraint("ck_charge_value", "\"Value\" > 0 AND \"Value\" <= 1000000 AND (\"Kind\" <> 'Percentage' OR \"Value\" <= 100)");
                    table.ForeignKey(
                        name: "FK_charge_rules_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charge_rules_AspNetUsers_StoppedBy",
                        column: x => x.StoppedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "charge_medicines",
                columns: table => new
                {
                    ChargeRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charge_medicines", x => new { x.ChargeRuleId, x.MedicineId });
                    table.ForeignKey(
                        name: "FK_charge_medicines_charge_rules_ChargeRuleId",
                        column: x => x.ChargeRuleId,
                        principalTable: "charge_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charge_medicines_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charge_medicines_MedicineId",
                table: "charge_medicines",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_charge_rules_CreatedBy",
                table: "charge_rules",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_charge_rules_NameKey",
                table: "charge_rules",
                column: "NameKey",
                unique: true,
                filter: "\"StoppedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_charge_rules_StoppedBy",
                table: "charge_rules",
                column: "StoppedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charge_medicines");

            migrationBuilder.DropTable(
                name: "charge_rules");
        }
    }
}
