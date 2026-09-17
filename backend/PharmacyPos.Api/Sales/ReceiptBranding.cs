using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Sales;

// Immutable revisions keep historical receipts unchanged without duplicating images per sale.
public sealed class ReceiptBranding
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Pharmacy POS";
    public string Address { get; set; } = "";
    public string? Logo { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
public static class ReceiptSettings
{
    public const string Policy = "Medicines cannot be returned or refunded after 15 days from purchase. The original receipt must be presented for any return or refund.";
    public sealed record Input(Guid Id, Guid? ExpectedId, string? Name, string? Address, string? Logo);
    public static object View(ReceiptBranding? b) => new { id = b?.Id, name = b?.Name ?? "Pharmacy POS", address = b?.Address ?? "", logo = b?.Logo, policy = Policy };
    public static Task<ReceiptBranding?> Current(PharmacyDbContext db, CancellationToken ct) => db.Set<ReceiptBranding>().OrderByDescending(b => b.At).ThenByDescending(b => b.Id).FirstOrDefaultAsync(ct);
    public static bool ValidLogo(string? logo)
    {
        if (logo == null) return true;
        const string prefix = "data:image/png;base64,";
        if (!logo.StartsWith(prefix, StringComparison.Ordinal) || logo.Length > 140000) return false;
        try {
            var bytes = Convert.FromBase64String(logo[prefix.Length..]);
            if (bytes.Length > 102400 || bytes.Length < 33 || !bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) || !bytes.AsSpan(12,4).SequenceEqual("IHDR"u8)) return false;
            var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4));
            return width is > 0 and <= 1024 && height is > 0 and <= 1024;
        } catch (FormatException) { return false; }
    }
    public static void MapReceiptSettings(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapGet("/api/receipt-settings", async (PharmacyDbContext db, HttpContext http, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(View(await Current(db, ct)));
        }).RequireAuthorization("AdminOnly");
        app.MapPost("/api/receipt-settings", async (Input input, PharmacyDbContext db, HttpContext http, IAntiforgery csrf, CancellationToken ct) => {
            try {
                await csrf.ValidateRequestAsync(http);
                var name = input.Name?.Trim() ?? ""; var address = input.Address?.Trim() ?? "";
                if (input.Id == Guid.Empty || name.Length is < 1 or > 200 || address.Length is < 1 or > 600 || !ValidLogo(input.Logo))
                    return Results.BadRequest(new { message = "Enter the name and address. Use a PNG logo up to 100 KB and 1024 × 1024 pixels." });
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425918)", ct);
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var previous = await db.Set<ReceiptBranding>().FindAsync([input.Id], ct);
                if (previous != null) return previous.ActorId == actor && previous.Name == name && previous.Address == address && previous.Logo == input.Logo
                    ? Results.Ok(View(previous)) : Results.Conflict(new { message = "This save reference was already used. Reload settings." });
                if ((await Current(db, ct))?.Id != input.ExpectedId) return Results.Conflict(new { message = "Receipt settings changed. Reload before saving." });
                var b = new ReceiptBranding { Id = input.Id, Name = name, Address = address, Logo = input.Logo, ActorId = actor, ActorName = http.User.Identity?.Name ?? actor, At = DateTimeOffset.UtcNow };
                db.Add(b); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Ok(View(b));
            } catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Refresh your sign-in before saving." }); }
        }).RequireAuthorization("AdminOnly");
    }
}
