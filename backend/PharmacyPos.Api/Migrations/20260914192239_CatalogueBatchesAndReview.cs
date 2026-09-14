using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueBatchesAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_medicine_base_unit",
                table: "medicines");

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "medicines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_status",
                table: "medicines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "medicines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by_user_id",
                table: "medicines",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "medicine_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medicine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_batches", x => x.id);
                    table.CheckConstraint("ck_batch_dates", "manufacturing_date IS NULL OR manufacturing_date <= expiry_date");
                    table.CheckConstraint("ck_batch_number", "length(btrim(batch_number)) > 0");
                    table.ForeignKey(
                        name: "FK_medicine_batches_medicines_medicine_id",
                        column: x => x.medicine_id,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medicines_reviewed_by_user_id",
                table: "medicines",
                column: "reviewed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_medicine_base_unit",
                table: "medicines",
                sql: "base_unit IN ('Tablet', 'Capsule', 'Bottle', 'Tube', 'Vial', 'Ampoule', 'Sachet', 'Piece')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_medicine_review",
                table: "medicines",
                sql: "review_status IN ('Approved', 'PendingReview', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_batches_medicine_id_batch_number",
                table: "medicine_batches",
                columns: new[] { "medicine_id", "batch_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_medicines_AspNetUsers_reviewed_by_user_id",
                table: "medicines",
                column: "reviewed_by_user_id",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_medicines_AspNetUsers_reviewed_by_user_id",
                table: "medicines");

            migrationBuilder.DropTable(
                name: "medicine_batches");

            migrationBuilder.DropIndex(
                name: "IX_medicines_reviewed_by_user_id",
                table: "medicines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_medicine_base_unit",
                table: "medicines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_medicine_review",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "review_status",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "medicines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_medicine_base_unit",
                table: "medicines",
                sql: "base_unit = 'Tablet'");
        }
    }
}
