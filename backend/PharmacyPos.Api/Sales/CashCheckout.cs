using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Sales;

public sealed class Sale
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public Guid? BrandingId { get; set; }
    public ReceiptBranding? Branding { get; set; }
    public string OperatorId { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
    public string RequestHash { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public decimal Total { get; set; }
    public List<SalePayment> Payments { get; set; } = [];
}
public sealed class SalePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public string Method { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public decimal Tendered { get; set; }
    public decimal Change { get; set; }
}
public sealed class SaleStockMovement
{
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public Guid ReceiptId { get; set; }
    public StockReceipt Receipt { get; set; } = null!;
    public long Quantity { get; set; }
}
public static class CashCheckout
{
    public sealed record Input(Guid RequestId, List<SalesCounter.CartLine>? Lines, string? QuoteHash, decimal CashReceived);
    public static void MapSaleData(this ModelBuilder model)
    {
        var sale = model.Entity<Sale>(); sale.ToTable("sales"); sale.HasKey(s => s.Id);
        sale.Property(s => s.Number).UseIdentityAlwaysColumn(); sale.HasIndex(s => s.Number).IsUnique();
        sale.Property(s => s.Snapshot).HasColumnType("jsonb"); sale.Property(s => s.RequestHash).HasMaxLength(64);
        sale.Property(s => s.OperatorName).HasMaxLength(256); sale.Property(s => s.Total).HasPrecision(18,2);
        sale.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.OperatorId).OnDelete(DeleteBehavior.Restrict);
        sale.HasIndex(s => new { s.OperatorId, s.CompletedAt });
        var p = model.Entity<SalePayment>(); p.ToTable("sale_payments", t => t.HasCheckConstraint("ck_cash_payment", "\"Amount\" >= 0 AND \"Tendered\" >= \"Amount\" AND \"Change\" = \"Tendered\" - \"Amount\""));
        p.HasKey(s => s.Id); p.Property(s => s.Method).HasMaxLength(30);
        p.Property(s => s.Amount).HasPrecision(18,2); p.Property(s => s.Tendered).HasPrecision(18,2); p.Property(s => s.Change).HasPrecision(18,2);
        p.HasOne(s => s.Sale).WithMany(s => s.Payments).HasForeignKey(s => s.SaleId).OnDelete(DeleteBehavior.Restrict);
        var m = model.Entity<SaleStockMovement>(); m.ToTable("sale_stock_movements", t => t.HasCheckConstraint("ck_sale_quantity", "\"Quantity\" > 0"));
        m.HasKey(s => new { s.SaleId, s.ReceiptId });
        m.HasOne(s => s.Sale).WithMany().HasForeignKey(s => s.SaleId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne(s => s.Receipt).WithMany().HasForeignKey(s => s.ReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
    private static object Receipt(Sale sale) => new { sale.Id, receiptNumber = $"POS-{sale.Number:D8}", sale.CompletedAt, sale.OperatorName,
        branding = ReceiptSettings.View(sale.Branding), returnDeadline = sale.CompletedAt.AddDays(15), sale.Total, currency = "BDT", pricing = JsonSerializer.Deserialize<JsonElement>(sale.Snapshot),
        payments = sale.Payments.Select(p => new { p.Method, p.Amount, p.Tendered, p.Change }) };
    public static void MapCashCheckout(this WebApplication app)
    {

        app.MapPost("/api/sales/checkout", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            try {
                await csrf.ValidateRequestAsync(http);
                if (input.RequestId == Guid.Empty || input.QuoteHash?.Length != 64 || input.CashReceived < 0 || input.CashReceived > 1000000000000m || decimal.Round(input.CashReceived,2) != input.CashReceived)
                    return Results.BadRequest(new { message = "Enter a valid cash amount with up to two decimals and refresh cart prices." });
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, SalesCounter.JsonOptions))));
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                // Same order as receiving; all stock and pricing mutations serialize with checkout.
                foreach (var key in new[] { 718425911, 718425912, 718425913, 718425914 })
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
                var existing = await db.Sales.Include(s => s.Payments).Include(s => s.Branding).SingleOrDefaultAsync(s => s.Id == input.RequestId, ct);
                if (existing != null) return existing.OperatorId == actor && existing.RequestHash == hash ? Results.Ok(Receipt(existing))
                    : Results.Conflict(new { message = "This checkout reference was already used. Check recent sales before starting another sale." });
                var quote = await SalesCounter.BuildQuote(new(input.Lines), db, ct);
                if (quote.QuoteHash != input.QuoteHash) return Results.Conflict(new { message = "Stock or pricing changed. No sale was made. Refresh prices and check the new total before collecting cash." });
                if (input.CashReceived < quote.Payable) return Results.BadRequest(new { message = "Cash received is less than the amount due. No sale was made." });
                var sale = new Sale { Id = input.RequestId, OperatorId = actor, OperatorName = http.User.Identity?.Name ?? actor,
                    Branding = await ReceiptSettings.Current(db, ct), CompletedAt = DateTimeOffset.UtcNow, RequestHash = hash, Snapshot = quote.Snapshot.GetRawText(), Total = quote.Payable,
                    Payments = [new() { Method = "Cash", Amount = quote.Payable, Tendered = input.CashReceived, Change = input.CashReceived - quote.Payable }] };
                db.Sales.Add(sale);
                foreach (var (lotId, quantity) in quote.Quantities) db.SaleStockMovements.Add(new() { SaleId = sale.Id, ReceiptId = lotId, Quantity = quantity });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(Receipt(sale));
            } catch (SalesCounter.PricingFailure e) { return Results.Json(new { message = e.Message }, statusCode: e.Status); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh your sign-in before completing the sale." }); }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or OperationCanceledException) {
                return Results.Json(new { message = "The sale result is not confirmed. Retry this same checkout to recover its receipt; do not start a second sale." }, statusCode: 503);
            }
        }).RequireAuthorization("Staff");
        app.MapGet("/api/sales/receipts/{id:guid}", async (Guid id, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sale = await db.Sales.AsNoTracking().Include(s => s.Payments).Include(s => s.Branding).SingleOrDefaultAsync(s => s.Id == id && (http.User.IsInRole("Admin") || s.OperatorId == actor), ct);
            return sale == null ? Results.NotFound() : Results.Ok(Receipt(sale));
        }).RequireAuthorization("Staff");
        app.MapGet("/api/sales/receipts", async (int? page, PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            var n = page ?? 1; if (n is < 1 or > 10000) return Results.BadRequest();
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var rows = await db.Sales.AsNoTracking().Where(s => http.User.IsInRole("Admin") || s.OperatorId == actor)
                .OrderByDescending(s => s.CompletedAt).ThenByDescending(s => s.Number).Skip((n-1)*25).Take(26)
                .Select(s => new { s.Id, s.Number, s.CompletedAt, s.Total, s.OperatorName }).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25), hasMore = rows.Count > 25, page = n });
        }).RequireAuthorization("Staff");
    }
}
