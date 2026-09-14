namespace PharmacyPos.Api.Catalogue;

public enum MedicineClassification { Otc, Prescription }

public enum StockUnit { Tablet, Capsule, Bottle, Tube, Vial, Ampoule, Sachet, Piece }
public enum CatalogueReviewStatus { Approved, PendingReview, Rejected }

public sealed class Medicine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string BrandName { get; set; }
    public Guid ManufacturerId { get; set; }
    public Manufacturer Manufacturer { get; set; } = null!;
    public Guid DosageFormId { get; set; }
    public DosageForm DosageForm { get; set; } = null!;
    public required MedicineClassification Classification { get; set; }
    public StockUnit BaseUnit { get; set; } = StockUnit.Tablet;
    public bool IsActive { get; set; } = true;
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreationRequestHash { get; set; }
    public CatalogueReviewStatus ReviewStatus { get; set; } = CatalogueReviewStatus.Approved;
    public string? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public List<MedicineBatch> Batches { get; set; } = [];
    public List<MedicineIngredient> Ingredients { get; set; } = [];
}
