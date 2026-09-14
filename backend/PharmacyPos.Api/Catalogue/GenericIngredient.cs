namespace PharmacyPos.Api.Catalogue;

public sealed class GenericIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}
