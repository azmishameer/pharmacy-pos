using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChargeProfitClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClassificationVersion",
                table: "charge_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromProfit",
                table: "charge_rules",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InitialExcludeFromProfit",
                table: "charge_rules",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "charge_classification_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousValue = table.Column<bool>(type: "boolean", nullable: true),
                    ExcludeFromProfit = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charge_classification_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_charge_classification_events_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charge_classification_events_charge_rules_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "charge_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charge_classification_events_ActorId",
                table: "charge_classification_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_charge_classification_events_ChargeId_At",
                table: "charge_classification_events",
                columns: new[] { "ChargeId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charge_classification_events");

            migrationBuilder.DropColumn(
                name: "ClassificationVersion",
                table: "charge_rules");

            migrationBuilder.DropColumn(
                name: "ExcludeFromProfit",
                table: "charge_rules");

            migrationBuilder.DropColumn(
                name: "InitialExcludeFromProfit",
                table: "charge_rules");
        }
    }
}
