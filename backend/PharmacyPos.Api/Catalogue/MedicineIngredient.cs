namespace PharmacyPos.Api.Catalogue;

public sealed class MedicineIngredient
{
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public Guid GenericIngredientId { get; set; }
    public GenericIngredient GenericIngredient { get; set; } = null!;
    public decimal StrengthValue { get; set; }
    public required string StrengthUnit { get; set; }
    public int DisplayOrder { get; set; }
}
