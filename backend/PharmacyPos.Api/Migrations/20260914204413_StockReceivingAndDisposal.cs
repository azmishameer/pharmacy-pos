using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyPos.Api.Migrations
{
    /// <inheritdoc />
    public partial class StockReceivingAndDisposal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_export_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_export_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_export_events_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_receipts", x => x.Id);
                    table.CheckConstraint("ck_receipt_revision", "\"CurrentRevision\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_disposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DisposedByUserId = table.Column<string>(type: "text", nullable: false),
                    DisposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VisibleUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_disposals", x => x.Id);
                    table.CheckConstraint("ck_disposal_quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("ck_disposal_retention", "\"VisibleUntil\" > \"DisposedAt\"");
                    table.ForeignKey(
                        name: "FK_stock_disposals_AspNetUsers_DisposedByUserId",
                        column: x => x.DisposedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_disposals_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipt_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeliveryReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BaseUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitsPerStrip = table.Column<int>(type: "integer", nullable: false),
                    StripsPerBox = table.Column<int>(type: "integer", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "integer", nullable: false),
                    BoxesPerCarton = table.Column<int>(type: "integer", nullable: false),
                    Pieces = table.Column<int>(type: "integer", nullable: false),
                    Strips = table.Column<int>(type: "integer", nullable: false),
                    Boxes = table.Column<int>(type: "integer", nullable: false),
                    Cartons = table.Column<int>(type: "integer", nullable: false),
                    TotalUnits = table.Column<long>(type: "bigint", nullable: false),
                    MrpAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    MrpUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MrpUnits = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AutomaticApproval = table.Column<bool>(type: "boolean", nullable: false),
                    MrpVerifiedByUserId = table.Column<string>(type: "text", nullable: true),
                    MrpVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_receipt_revisions", x => x.Id);
                    table.UniqueConstraint("AK_stock_receipt_revisions_Id_ReceiptId", x => new { x.Id, x.ReceiptId });
                    table.CheckConstraint("ck_receiving_dates", "\"ManufacturingDate\" IS NULL OR \"ManufacturingDate\" <= \"ExpiryDate\"");
                    table.CheckConstraint("ck_receiving_pack", "\"UnitsPerStrip\" >= 0 AND \"StripsPerBox\" >= 0 AND \"UnitsPerBox\" BETWEEN 1 AND 1000000 AND \"BoxesPerCarton\" BETWEEN 1 AND 1000000 AND ((\"UnitsPerStrip\" = 0 AND \"StripsPerBox\" = 0 AND \"Strips\" = 0) OR (\"UnitsPerStrip\" > 0 AND \"StripsPerBox\" > 0 AND \"UnitsPerStrip\"::bigint * \"StripsPerBox\" = \"UnitsPerBox\"))");
                    table.CheckConstraint("ck_receiving_price", "\"MrpAmount\" > 0 AND \"MrpUnit\" IN ('Piece', 'Strip', 'Box') AND \"MrpUnits\" = CASE \"MrpUnit\" WHEN 'Piece' THEN 1 WHEN 'Strip' THEN \"UnitsPerStrip\" ELSE \"UnitsPerBox\" END AND \"MrpUnits\" > 0");
                    table.CheckConstraint("ck_receiving_quantity", "\"Pieces\" >= 0 AND \"Strips\" >= 0 AND \"Boxes\" >= 0 AND \"Cartons\" >= 0 AND \"TotalUnits\" BETWEEN 1 AND 1000000000 AND \"TotalUnits\" = \"Pieces\"::bigint + \"Strips\"::bigint * \"UnitsPerStrip\" + (\"Boxes\"::bigint + \"Cartons\"::bigint * \"BoxesPerCarton\") * \"UnitsPerBox\"");
                    table.CheckConstraint("ck_receiving_revision", "\"Revision\" > 0");
                    table.CheckConstraint("ck_receiving_status", "\"Status\" IN ('PendingApproval', 'Returned', 'Superseded', 'Approved')");
                    table.ForeignKey(
                        name: "FK_stock_receipt_revisions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_receipt_revisions_AspNetUsers_MrpVerifiedByUserId",
                        column: x => x.MrpVerifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_receipt_revisions_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_receipt_revisions_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_receipt_revisions_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receiving_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    PostedByUserId = table.Column<string>(type: "text", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receiving_movements", x => x.Id);
                    table.CheckConstraint("ck_receiving_movement_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_receiving_movements_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_movements_medicine_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "medicine_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receiving_movements_stock_receipt_revisions_RevisionId_Rece~",
                        columns: x => new { x.RevisionId, x.ReceiptId },
                        principalTable: "stock_receipt_revisions",
                        principalColumns: new[] { "Id", "ReceiptId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_review_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_review_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_review_events_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_review_events_stock_receipt_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "stock_receipt_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receiving_movements_BatchId",
                table: "receiving_movements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_movements_PostedByUserId",
                table: "receiving_movements",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receiving_movements_ReceiptId",
                table: "receiving_movements",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receiving_movements_RevisionId_ReceiptId",
                table: "receiving_movements",
                columns: new[] { "RevisionId", "ReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_disposals_DisposedByUserId",
                table: "stock_disposals",
                column: "DisposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_disposals_ReceiptId",
                table: "stock_disposals",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_disposals_VisibleUntil",
                table: "stock_disposals",
                column: "VisibleUntil");

            migrationBuilder.CreateIndex(
                name: "IX_stock_export_events_ActorId",
                table: "stock_export_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_CreatedByUserId",
                table: "stock_receipt_revisions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_MedicineId",
                table: "stock_receipt_revisions",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_MrpVerifiedByUserId",
                table: "stock_receipt_revisions",
                column: "MrpVerifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_ReceiptId_Revision",
                table: "stock_receipt_revisions",
                columns: new[] { "ReceiptId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_ReviewedByUserId",
                table: "stock_receipt_revisions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_revisions_Status_CreatedAt",
                table: "stock_receipt_revisions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_review_events_ActorId",
                table: "stock_review_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_review_events_RevisionId",
                table: "stock_review_events",
                column: "RevisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receiving_movements");

            migrationBuilder.DropTable(
                name: "stock_disposals");

            migrationBuilder.DropTable(
                name: "stock_export_events");

            migrationBuilder.DropTable(
                name: "stock_review_events");

            migrationBuilder.DropTable(
                name: "stock_receipt_revisions");

            migrationBuilder.DropTable(
                name: "stock_receipts");
        }
    }
}
