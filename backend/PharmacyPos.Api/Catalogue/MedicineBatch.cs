namespace PharmacyPos.Api.Catalogue;

// Batch identification only: this record does not create available stock.
public sealed class MedicineBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public required string BatchNumber { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
}
