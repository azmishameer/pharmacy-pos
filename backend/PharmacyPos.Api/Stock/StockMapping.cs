using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PharmacyPos.Api.Stock;

public static class StockMapping
{
    public static void MapStock(this ModelBuilder model)
    {
        var disposal = model.Entity<StockDisposal>();
        disposal.ToTable("stock_disposals", t => {
            t.HasCheckConstraint("ck_disposal_quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("ck_disposal_retention", "\"VisibleUntil\" > \"DisposedAt\"");
        });
        disposal.HasKey(x => x.Id);
        disposal.HasIndex(x => x.ReceiptId).IsUnique();
        disposal.HasIndex(x => x.VisibleUntil);
        disposal.Property(x => x.Reason).HasMaxLength(1000);
        disposal.HasOne(x => x.Receipt).WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        disposal.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.DisposedByUserId).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<StockReceipt>();
        receipt.ToTable("stock_receipts", t => t.HasCheckConstraint("ck_receipt_revision", "\"CurrentRevision\" > 0"));
        receipt.HasKey(x => x.Id);
        var r = model.Entity<StockReceiptRevision>();
        r.ToTable("stock_receipt_revisions", t => {
            t.HasCheckConstraint("ck_receiving_revision", "\"Revision\" > 0");
            t.HasCheckConstraint("ck_receiving_status", "\"Status\" IN ('PendingApproval', 'Returned', 'Superseded', 'Approved')");
            t.HasCheckConstraint("ck_receiving_dates", "\"ManufacturingDate\" IS NULL OR \"ManufacturingDate\" <= \"ExpiryDate\"");
            t.HasCheckConstraint("ck_receiving_pack", "\"UnitsPerStrip\" >= 0 AND \"StripsPerBox\" >= 0 AND \"UnitsPerBox\" BETWEEN 1 AND 1000000 AND \"BoxesPerCarton\" BETWEEN 1 AND 1000000 AND ((\"UnitsPerStrip\" = 0 AND \"StripsPerBox\" = 0 AND \"Strips\" = 0) OR (\"UnitsPerStrip\" > 0 AND \"StripsPerBox\" > 0 AND \"UnitsPerStrip\"::bigint * \"StripsPerBox\" = \"UnitsPerBox\"))");
            t.HasCheckConstraint("ck_receiving_quantity", "\"Pieces\" >= 0 AND \"Strips\" >= 0 AND \"Boxes\" >= 0 AND \"Cartons\" >= 0 AND \"TotalUnits\" BETWEEN 1 AND 1000000000 AND \"TotalUnits\" = \"Pieces\"::bigint + \"Strips\"::bigint * \"UnitsPerStrip\" + (\"Boxes\"::bigint + \"Cartons\"::bigint * \"BoxesPerCarton\") * \"UnitsPerBox\"");
            t.HasCheckConstraint("ck_receiving_price", "\"MrpAmount\" > 0 AND \"MrpUnit\" IN ('Piece', 'Strip', 'Box') AND \"MrpUnits\" = CASE \"MrpUnit\" WHEN 'Piece' THEN 1 WHEN 'Strip' THEN \"UnitsPerStrip\" ELSE \"UnitsPerBox\" END AND \"MrpUnits\" > 0");
        });
        r.HasKey(x => x.Id);
        r.HasAlternateKey(x => new { x.Id, x.ReceiptId });
        r.HasIndex(x => new { x.ReceiptId, x.Revision }).IsUnique();
        r.HasIndex(x => new { x.Status, x.CreatedAt });
        r.HasOne(x => x.Receipt).WithMany(x => x.Revisions).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.Medicine).WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        r.Property(x => x.BaseUnit).HasConversion<string>().HasMaxLength(20);
        r.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        r.Property(x => x.RequestHash).HasMaxLength(64);
        r.Property(x => x.Supplier).HasMaxLength(200);
        r.Property(x => x.DeliveryReference).HasMaxLength(100);
        r.Property(x => x.BatchNumber).HasMaxLength(100);
        r.Property(x => x.CorrectionReason).HasMaxLength(1000);
        r.Property(x => x.ReviewNote).HasMaxLength(1000);
        r.Property(x => x.MrpUnit).HasMaxLength(10);
        r.Property(x => x.MrpAmount).HasPrecision(14, 2);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.MrpVerifiedByUserId).OnDelete(DeleteBehavior.Restrict);
        var m = model.Entity<ReceivingMovement>();
        m.ToTable("receiving_movements", t => t.HasCheckConstraint("ck_receiving_movement_quantity", "\"Quantity\" > 0"));
        m.HasKey(x => x.Id);
        m.HasIndex(x => x.ReceiptId).IsUnique();
        m.HasOne(x => x.Revision).WithMany().HasForeignKey(x => new { x.RevisionId, x.ReceiptId }).HasPrincipalKey(x => new { x.Id, x.ReceiptId }).OnDelete(DeleteBehavior.Restrict);
        m.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        var e = model.Entity<StockReviewEvent>();
        e.ToTable("stock_review_events"); e.HasKey(x => x.Id);
        e.Property(x => x.Action).HasMaxLength(30); e.Property(x => x.Note).HasMaxLength(1000);
        e.HasOne(x => x.Revision).WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var export = model.Entity<StockExportEvent>();
        export.ToTable("stock_export_events"); export.HasKey(x => x.Id);
        export.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
