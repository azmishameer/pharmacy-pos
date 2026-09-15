using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class DiscountOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offer_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MinimumSubtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoppedBy = table.Column<string>(type: "text", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_rules", x => x.Id);
                    table.CheckConstraint("ck_offer_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("ck_offer_kind", "\"Kind\" IN ('Percentage', 'Fixed')");
                    table.CheckConstraint("ck_offer_minimum", "\"MinimumSubtotal\" BETWEEN 0 AND 1000000000 AND (\"MedicineId\" IS NULL OR \"MinimumSubtotal\" = 0)");
                    table.CheckConstraint("ck_offer_pack_scope", "(\"MedicineId\" IS NOT NULL AND \"Kind\" = 'Fixed') OR (\"Unit\" = 'Piece' AND \"Units\" = 1)");
                    table.CheckConstraint("ck_offer_unit", "\"Unit\" IN ('Piece', 'Strip', 'Box') AND \"Units\" BETWEEN 1 AND 1000000 AND (\"Unit\" <> 'Piece' OR \"Units\" = 1)");
                    table.CheckConstraint("ck_offer_value", "\"Value\" > 0 AND \"Value\" <= 1000000 AND (\"Kind\" <> 'Percentage' OR \"Value\" <= 100)");
                    table.ForeignKey(
                        name: "FK_offer_rules_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_offer_rules_AspNetUsers_StoppedBy",
                        column: x => x.StoppedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_offer_rules_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_rules_CreatedBy",
                table: "offer_rules",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_offer_rules_MedicineId",
                table: "offer_rules",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_offer_rules_NameKey",
                table: "offer_rules",
                column: "NameKey",
                unique: true,
                filter: "\"StoppedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_offer_rules_StoppedBy",
                table: "offer_rules",
                column: "StoppedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_rules");
        }
    }
}
