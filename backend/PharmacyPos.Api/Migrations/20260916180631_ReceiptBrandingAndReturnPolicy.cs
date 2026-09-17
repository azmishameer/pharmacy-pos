using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptBrandingAndReturnPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BrandingId",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptPresented",
                table: "sale_returns",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receipt_branding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Logo = table.Column<string>(type: "character varying(140000)", maxLength: 140000, nullable: true),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_branding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_branding_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_BrandingId",
                table: "sales",
                column: "BrandingId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_branding_ActorId",
                table: "receipt_branding",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_branding_At",
                table: "receipt_branding",
                column: "At");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_receipt_branding_BrandingId",
                table: "sales",
                column: "BrandingId",
                principalTable: "receipt_branding",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_receipt_branding_BrandingId",
                table: "sales");

            migrationBuilder.DropTable(
                name: "receipt_branding");

            migrationBuilder.DropIndex(
                name: "IX_sales_BrandingId",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "BrandingId",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "ReceiptPresented",
                table: "sale_returns");
        }
    }
}
