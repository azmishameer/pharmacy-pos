using PharmacyPos.Api.Catalogue;

namespace PharmacyPos.Api.Stock;

public enum ReceivingStatus { PendingApproval, Returned, Superseded, Approved }

// Stable receiving-line identity. Revisions preserve corrections; only one can post stock.
public sealed class StockReceipt
{
    public Guid Id { get; set; }
    public int CurrentRevision { get; set; }
    public List<StockReceiptRevision> Revisions { get; set; } = [];
}

public sealed class StockReceiptRevision
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public StockReceipt Receipt { get; set; } = null!;
    public int Revision { get; set; }
    public required string RequestHash { get; set; }
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public required string Supplier { get; set; }
    public string? DeliveryReference { get; set; }
    public DateOnly ReceivedDate { get; set; }
    // Unapproved batch data stays here; only approval creates/reuses canonical batch metadata.
    public required string BatchNumber { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public StockUnit BaseUnit { get; set; }
    public int UnitsPerStrip { get; set; }
    public int StripsPerBox { get; set; }
    public int UnitsPerBox { get; set; }
    public int BoxesPerCarton { get; set; }
    public int Pieces { get; set; }
    public int Strips { get; set; }
    public int Boxes { get; set; }
    public int Cartons { get; set; }
    public long TotalUnits { get; set; }
    // Preserve a rational price (amount / units), not a rounded per-unit rate.
    public decimal MrpAmount { get; set; }
    public required string MrpUnit { get; set; }
    public int MrpUnits { get; set; }
    public ReceivingStatus Status { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CorrectionReason { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public bool AutomaticApproval { get; set; }
    public string? MrpVerifiedByUserId { get; set; }
    public DateTimeOffset? MrpVerifiedAt { get; set; }
}

// An approved receipt is a separately priced lot, even if another delivery shares its batch.
// This prevents a new delivery's printed price or pack sizes from altering older inventory.
public sealed class ReceivingMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptId { get; set; }
    public Guid RevisionId { get; set; }
    public StockReceiptRevision Revision { get; set; } = null!;
    public Guid BatchId { get; set; }
    public MedicineBatch Batch { get; set; } = null!;
    public long Quantity { get; set; }
    public required string PostedByUserId { get; set; }
    public DateTimeOffset PostedAt { get; set; }
}

public sealed class StockReviewEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public StockReceiptRevision Revision { get; set; } = null!;
    public required string Action { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Note { get; set; }
}

public sealed class StockExportEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public int RowCount { get; set; }
}

// Append-only outgoing quantity after physical disposal. Never delete the receiving history.
public sealed class StockDisposal
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public StockReceipt Receipt { get; set; } = null!;
    public long Quantity { get; set; }
    public required string Reason { get; set; }
    public required string DisposedByUserId { get; set; }
    public DateTimeOffset DisposedAt { get; set; }
    public DateTimeOffset VisibleUntil { get; set; }
}
