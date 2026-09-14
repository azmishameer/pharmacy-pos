using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueCreationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "medicines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "medicines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "creation_request_hash",
                table: "medicines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_created_by_user_id",
                table: "medicines",
                column: "created_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_medicines_AspNetUsers_created_by_user_id",
                table: "medicines",
                column: "created_by_user_id",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_medicines_AspNetUsers_created_by_user_id",
                table: "medicines");

            migrationBuilder.DropIndex(
                name: "IX_medicines_created_by_user_id",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "medicines");

            migrationBuilder.DropColumn(
                name: "creation_request_hash",
                table: "medicines");
        }
    }
}
