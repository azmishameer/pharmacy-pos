using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class SplitPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundDestination",
                table: "sale_returns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cash");

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "sale_payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceKey",
                table: "sale_payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "return_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_payments", x => x.Id);
                    table.CheckConstraint("ck_return_payment", "\"Amount\" >= 0 AND (\"Method\" = 'Cash' AND \"Reference\" IS NULL OR \"Method\" IN ('Card','bKash','Nagad') AND \"Reference\" IS NOT NULL AND length(btrim(\"Reference\")) > 0)");
                    table.ForeignKey(
                        name: "FK_return_payments_sale_returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "sale_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_Method_ReferenceKey",
                table: "sale_payments",
                columns: new[] { "Method", "ReferenceKey" },
                unique: true,
                filter: "\"ReferenceKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_method",
                table: "sale_payments",
                sql: "\"Method\" = 'Cash' AND \"Reference\" IS NULL AND \"ReferenceKey\" IS NULL OR \"Method\" IN ('Card','bKash','Nagad') AND \"Reference\" IS NOT NULL AND length(btrim(\"Reference\")) > 0 AND \"ReferenceKey\" IS NOT NULL AND \"Tendered\" = \"Amount\" AND \"Change\" = 0");

            // Existing refunds were all recorded as cash. Preserve their payment audit.
            migrationBuilder.Sql("""
                INSERT INTO return_payments ("Id", "ReturnId", "Method", "Amount", "Reference")
                SELECT gen_random_uuid(), "Id", 'Cash', "Amount", NULL
                FROM sale_returns WHERE "Method" = 'Cash';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_return_payments_ReturnId_Method",
                table: "return_payments",
                columns: new[] { "ReturnId", "Method" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_payments");

            migrationBuilder.DropIndex(
                name: "IX_sale_payments_Method_ReferenceKey",
                table: "sale_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_method",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "RefundDestination",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "ReferenceKey",
                table: "sale_payments");
        }
    }
}
