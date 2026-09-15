using PharmacyPos.Api.Auth;
using PharmacyPos.Api.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Sales;

namespace PharmacyPos.Api.Data;

public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<StaffAccount> StaffAccounts => Set<StaffAccount>();
    public DbSet<StaffAccountEvent> StaffAccountEvents => Set<StaffAccountEvent>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<ReturnedItem> ReturnedItems => Set<ReturnedItem>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleStockMovement> SaleStockMovements => Set<SaleStockMovement>();
    public DbSet<OfferRule> OfferRules => Set<OfferRule>();
    public DbSet<ChargeRule> ChargeRules => Set<ChargeRule>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<GenericIngredient> GenericIngredients => Set<GenericIngredient>();
    public DbSet<DosageForm> DosageForms => Set<DosageForm>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineIngredient> MedicineIngredients => Set<MedicineIngredient>();

    public DbSet<MedicineBatch> MedicineBatches => Set<MedicineBatch>();

    public DbSet<StockDisposal> StockDisposals => Set<StockDisposal>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<StockReceiptRevision> StockReceiptRevisions => Set<StockReceiptRevision>();
    public DbSet<ReceivingMovement> ReceivingMovements => Set<ReceivingMovement>();
    public DbSet<StockReviewEvent> StockReviewEvents => Set<StockReviewEvent>();
    public DbSet<StockExportEvent> StockExportEvents => Set<StockExportEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.MapStock();
        modelBuilder.MapCharges();
        modelBuilder.MapOffers();
        modelBuilder.MapSaleData();
        modelBuilder.MapReturns();
        modelBuilder.MapStaffAccounts();
        var manufacturer = modelBuilder.Entity<Manufacturer>();
        manufacturer.ToTable("manufacturers", t => t.HasCheckConstraint("ck_manufacturer_name", "length(btrim(name)) > 0"));
        manufacturer.HasKey(x => x.Id);
        manufacturer.Property(x => x.Id).HasColumnName("id");
        manufacturer.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        manufacturer.Property(x => x.IsActive).HasColumnName("is_active");
        manufacturer.HasIndex(x => x.Name);

        var generic = modelBuilder.Entity<GenericIngredient>();
        generic.ToTable("generic_ingredients", t => t.HasCheckConstraint("ck_generic_name", "length(btrim(name)) > 0"));
        generic.HasKey(x => x.Id);
        generic.Property(x => x.Id).HasColumnName("id");
        generic.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        generic.Property(x => x.IsActive).HasColumnName("is_active");
        generic.HasIndex(x => x.Name);

        var form = modelBuilder.Entity<DosageForm>();
        form.ToTable("dosage_forms", t => t.HasCheckConstraint("ck_form_name", "length(btrim(name)) > 0"));
        form.HasKey(x => x.Id);
        form.Property(x => x.Id).HasColumnName("id");
        form.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        form.Property(x => x.IsActive).HasColumnName("is_active");
        form.HasIndex(x => x.Name);

        var medicine = modelBuilder.Entity<Medicine>();
        medicine.ToTable("medicines", t =>
        {
            t.HasCheckConstraint("ck_medicine_brand", "length(btrim(brand_name)) > 0");
            t.HasCheckConstraint("ck_medicine_classification", "classification IN ('Otc', 'Prescription')");
            t.HasCheckConstraint("ck_medicine_base_unit", "base_unit IN ('Tablet', 'Capsule', 'Bottle', 'Tube', 'Vial', 'Ampoule', 'Sachet', 'Piece')");
        });
        medicine.Property(x => x.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(20).HasDefaultValue(CatalogueReviewStatus.Approved);
        medicine.ToTable("medicines", t => t.HasCheckConstraint("ck_medicine_review", "review_status IN ('Approved', 'PendingReview', 'Rejected')"));
        medicine.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        medicine.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        medicine.Property(x => x.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        medicine.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        var batch = modelBuilder.Entity<MedicineBatch>();
        batch.ToTable("medicine_batches", t => {
            t.HasCheckConstraint("ck_batch_number", "length(btrim(batch_number)) > 0");
            t.HasCheckConstraint("ck_batch_dates", "manufacturing_date IS NULL OR manufacturing_date <= expiry_date");
        });
        batch.HasKey(x => x.Id);
        batch.Property(x => x.Id).HasColumnName("id");
        batch.Property(x => x.MedicineId).HasColumnName("medicine_id");
        batch.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(100);
        batch.Property(x => x.ManufacturingDate).HasColumnName("manufacturing_date");
        batch.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
        batch.HasIndex(x => new { x.MedicineId, x.BatchNumber }).IsUnique();
        batch.HasOne(x => x.Medicine).WithMany(x => x.Batches).HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        medicine.HasKey(x => x.Id);
        medicine.Property(x => x.Id).HasColumnName("id");
        medicine.Property(x => x.BrandName).HasColumnName("brand_name").HasMaxLength(200).IsRequired();
        medicine.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id");
        medicine.Property(x => x.DosageFormId).HasColumnName("dosage_form_id");
        medicine.Property(x => x.Classification).HasColumnName("classification").HasConversion<string>().HasMaxLength(20);
        medicine.Property(x => x.BaseUnit).HasColumnName("base_unit").HasConversion<string>().HasMaxLength(20);
        medicine.Property(x => x.IsActive).HasColumnName("is_active");
        // Nullable for catalogue records that predate authenticated entry.
        medicine.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        medicine.Property(x => x.CreatedAt).HasColumnName("created_at");
        medicine.Property(x => x.CreationRequestHash).HasColumnName("creation_request_hash").HasMaxLength(64);
        medicine.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        medicine.HasIndex(x => x.BrandName);
        medicine.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        medicine.HasOne(x => x.DosageForm).WithMany().HasForeignKey(x => x.DosageFormId).OnDelete(DeleteBehavior.Restrict);

        var ingredient = modelBuilder.Entity<MedicineIngredient>();
        ingredient.ToTable("medicine_ingredients", t =>
        {
            t.HasCheckConstraint("ck_ingredient_strength", "strength_value > 0");
            t.HasCheckConstraint("ck_ingredient_unit", "length(btrim(strength_unit)) > 0");
            t.HasCheckConstraint("ck_ingredient_order", "display_order >= 0");
        });
        ingredient.HasKey(x => new { x.MedicineId, x.GenericIngredientId });
        ingredient.Property(x => x.MedicineId).HasColumnName("medicine_id");
        ingredient.Property(x => x.GenericIngredientId).HasColumnName("generic_ingredient_id");
        ingredient.Property(x => x.StrengthValue).HasColumnName("strength_value").HasPrecision(18, 6);
        ingredient.Property(x => x.StrengthUnit).HasColumnName("strength_unit").HasMaxLength(30).IsRequired();
        ingredient.Property(x => x.DisplayOrder).HasColumnName("display_order");
        ingredient.HasIndex(x => new { x.MedicineId, x.DisplayOrder }).IsUnique();
        // Deactivate catalogue records instead of cascading deletion through their references.
        ingredient.HasOne(x => x.Medicine).WithMany(x => x.Ingredients).HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        ingredient.HasOne(x => x.GenericIngredient).WithMany().HasForeignKey(x => x.GenericIngredientId).OnDelete(DeleteBehavior.Restrict);
    }
}
