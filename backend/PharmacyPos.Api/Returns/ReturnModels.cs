using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Returns;

public sealed class SaleReturn
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public bool? ReceiptPresented { get; set; }
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorName { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public List<ReturnedItem> Items { get; set; } = [];
}
public sealed class ReturnedItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReturnId { get; set; }
    public SaleReturn Return { get; set; } = null!;
    public Guid SaleId { get; set; }
    public int LineIndex { get; set; }
    public Guid ReceiptId { get; set; }
    public StockReceipt Receipt { get; set; } = null!;
    public string BrandName { get; set; } = "";
    public string BatchNumber { get; set; } = "";
    public string BaseUnit { get; set; } = "";
    public int Packs { get; set; }
    public string Unit { get; set; } = "";
    public long Quantity { get; set; }
    public decimal Refund { get; set; }
    public string Status { get; set; } = "Held";
    public string? ReviewedBy { get; set; }
    public string? ReviewerName { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset? VisibleUntil { get; set; }
}
public static class ReturnMapping
{
    public static void MapReturns(this ModelBuilder model) {
        var r = model.Entity<SaleReturn>(); r.ToTable("sale_returns", t => t.HasCheckConstraint("ck_return_amount", "\"Amount\" >= 0")); r.HasKey(x => x.Id);
        r.HasAlternateKey(x => new { x.Id, x.SaleId }); r.Property(x => x.Amount).HasPrecision(18,2);
        r.Property(x => x.Method).HasMaxLength(30); r.Property(x => x.Reason).HasMaxLength(1000); r.Property(x => x.ActorName).HasMaxLength(256);
        r.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x => x.At);
        var i = model.Entity<ReturnedItem>(); i.ToTable("returned_items", t => {
            t.HasCheckConstraint("ck_returned_quantity", "\"Quantity\" > 0 AND \"Packs\" > 0 AND \"LineIndex\" >= 0 AND \"Refund\" >= 0");
            t.HasCheckConstraint("ck_returned_status", "\"Status\" IN ('Held','Restocked','Disposed')");
        }); i.HasKey(x => x.Id); i.HasIndex(x => new { x.SaleId, x.LineIndex }).IsUnique();
        i.HasIndex(x => new { x.ReceiptId, x.Status }); i.Property(x => x.Refund).HasPrecision(18,2);
        i.Property(x => x.BrandName).HasMaxLength(200); i.Property(x => x.BatchNumber).HasMaxLength(100);
        i.Property(x => x.BaseUnit).HasMaxLength(20); i.Property(x => x.Unit).HasMaxLength(20); i.Property(x => x.Status).HasMaxLength(20);
        i.Property(x => x.ReviewerName).HasMaxLength(256); i.Property(x => x.ReviewReason).HasMaxLength(1000);
        i.HasOne(x => x.Return).WithMany(x => x.Items).HasForeignKey(x => new { x.ReturnId, x.SaleId }).HasPrincipalKey(x => new { x.Id, x.SaleId }).OnDelete(DeleteBehavior.Restrict);
        i.HasOne(x => x.Receipt).WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        i.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
