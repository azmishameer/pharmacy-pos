using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dosage_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dosage_forms", x => x.id);
                    table.CheckConstraint("ck_form_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "generic_ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_ingredients", x => x.id);
                    table.CheckConstraint("ck_generic_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "manufacturers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.id);
                    table.CheckConstraint("ck_manufacturer_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dosage_form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicines", x => x.id);
                    table.CheckConstraint("ck_medicine_base_unit", "base_unit = 'Tablet'");
                    table.CheckConstraint("ck_medicine_brand", "length(btrim(brand_name)) > 0");
                    table.CheckConstraint("ck_medicine_classification", "classification IN ('Otc', 'Prescription')");
                    table.ForeignKey(
                        name: "FK_medicines_dosage_forms_dosage_form_id",
                        column: x => x.dosage_form_id,
                        principalTable: "dosage_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicines_manufacturers_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalTable: "manufacturers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medicine_ingredients",
                columns: table => new
                {
                    medicine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generic_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strength_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    strength_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_ingredients", x => new { x.medicine_id, x.generic_ingredient_id });
                    table.CheckConstraint("ck_ingredient_order", "display_order >= 0");
                    table.CheckConstraint("ck_ingredient_strength", "strength_value > 0");
                    table.CheckConstraint("ck_ingredient_unit", "length(btrim(strength_unit)) > 0");
                    table.ForeignKey(
                        name: "FK_medicine_ingredients_generic_ingredients_generic_ingredient~",
                        column: x => x.generic_ingredient_id,
                        principalTable: "generic_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicine_ingredients_medicines_medicine_id",
                        column: x => x.medicine_id,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dosage_forms_name",
                table: "dosage_forms",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_generic_ingredients_name",
                table: "generic_ingredients",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_name",
                table: "manufacturers",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_ingredients_generic_ingredient_id",
                table: "medicine_ingredients",
                column: "generic_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_ingredients_medicine_id_display_order",
                table: "medicine_ingredients",
                columns: new[] { "medicine_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_brand_name",
                table: "medicines",
                column: "brand_name");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_dosage_form_id",
                table: "medicines",
                column: "dosage_form_id");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_manufacturer_id",
                table: "medicines",
                column: "manufacturer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medicine_ingredients");

            migrationBuilder.DropTable(
                name: "generic_ingredients");

            migrationBuilder.DropTable(
                name: "medicines");

            migrationBuilder.DropTable(
                name: "dosage_forms");

            migrationBuilder.DropTable(
                name: "manufacturers");
        }
    }
}
