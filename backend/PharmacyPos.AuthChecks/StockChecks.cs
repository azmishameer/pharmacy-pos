using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

public static class StockChecks
{
    private static void Check(bool success, string name) { if (!success) throw new Exception("STOCK CHECK FAILED: " + name); }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object input) {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        message.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        return await client.SendAsync(message);
    }
    public static async Task Run(HttpClient admin, HttpClient op, IServiceProvider services, Guid medicineId)
    {
        var today = StockReceiving.ShopToday();
        var input = new StockReceiving.ReceiveInput(Guid.NewGuid(), medicineId, "=Formula supplier", "INV-001", today,
            "RECV-A", today.AddMonths(-6), today.AddYears(1), 10, 3, 30, 20, 5, 2, 1, 2, 600m, "Box");
        Check((await admin.PostAsJsonAsync("/api/stock/receipts", input)).StatusCode == HttpStatusCode.BadRequest, "receiving CSRF enforced");
        Check((await Post(op, "/api/stock/receipts", input with { VerifyMrp = true })).StatusCode == HttpStatusCode.Forbidden, "operator cannot verify price on creation");
        Check((await Post(op, "/api/stock/receipts", input with { Pieces = -1 })).StatusCode == HttpStatusCode.BadRequest, "negative quantity rejected");
        Check((await Post(op, "/api/stock/receipts", input with { UnitsPerBox = 31 })).StatusCode == HttpStatusCode.BadRequest, "invalid pack conversion rejected");
        Check((await Post(op, "/api/stock/receipts", input with { MrpAmount = 1.123m })).StatusCode == HttpStatusCode.BadRequest, "MRP precision validated");
        Check((await Post(op, "/api/stock/receipts", input with { ManufacturingDate = today.AddDays(1) })).StatusCode == HttpStatusCode.BadRequest, "future manufacturing date rejected");
        var created = await Post(op, "/api/stock/receipts", input);
        Check(created.StatusCode == HttpStatusCode.Created, "operator submits stock");
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Check(body.GetProperty("totalUnits").GetInt64() == 1255, "mixed cartons boxes strips singles convert exactly");
        Check(body.GetProperty("status").GetString() == "PendingApproval", "operator entry pending");
        Check((await Post(op, "/api/stock/receipts", input)).StatusCode == HttpStatusCode.OK, "creation retry idempotent");
        Check((await Post(op, "/api/stock/receipts", input with { Pieces = 6 })).StatusCode == HttpStatusCode.Conflict, "changed retry blocked");
        var inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory");
        Check(inventory.GetProperty("items").GetArrayLength() == 0, "pending receipt does not create stock");
        Check((await op.GetAsync("/api/stock/receipts/export")).StatusCode == HttpStatusCode.Forbidden, "CSV requires admin");
        var csv = await admin.GetStringAsync("/api/stock/receipts/export");
        Check(csv.Contains("'="), "CSV formula cell neutralized");
        Check(csv.Contains(input.RequestId.ToString()) && csv.Contains("1255"), "CSV includes revision and quantity");
        var url = $"/api/stock/receipts/{input.RequestId}/review";
        var approve = new StockReceiving.ReviewInput(input.RequestId, true);
        Check((await Post(op, url, approve)).StatusCode == HttpStatusCode.Forbidden, "operator cannot approve");
        Check((await admin.PostAsJsonAsync(url, approve)).StatusCode == HttpStatusCode.BadRequest, "approval requires CSRF");
        var approvals = await Task.WhenAll(Post(admin, url, approve), Post(admin, url, approve));
        Check(approvals.All(x => x.IsSuccessStatusCode), "concurrent approval retries safe");
        inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory");
        var item = inventory.GetProperty("items")[0];
        Check(item.GetProperty("receivedUnits").GetInt64() == 1255 && item.GetProperty("sellableUnits").GetInt64() == 0, "stock approval does not verify price");
        Check((await Post(op, $"/api/stock/receipts/{input.RequestId}/verify-mrp", new { revisionId = input.RequestId })).StatusCode == HttpStatusCode.Forbidden, "operator price verification blocked");
        Check((await Post(admin, $"/api/stock/receipts/{input.RequestId}/verify-mrp", new { revisionId = input.RequestId })).IsSuccessStatusCode, "admin verifies MRP separately");
        inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory?availability=available");
        Check(inventory.GetProperty("items")[0].GetProperty("sellableUnits").GetInt64() == 1255, "verified current stock sellable");
        // Different printed prices for separate lots must never rewrite the first lot.
        var own = input with { RequestId = Guid.NewGuid(), Supplier = "Supplier", Pieces = 1, Strips = 0, Boxes = 0, Cartons = 0, MrpAmount = 21m, MrpUnit = "Piece", VerifyMrp = true };
        Check((await Post(admin, "/api/stock/receipts", own)).StatusCode == HttpStatusCode.Created, "admin entry autoapproved");
        var hidden = input with { RequestId = Guid.NewGuid(), BatchNumber = "Pending extra", Pieces = 0 };
        Check((await Post(op, "/api/stock/receipts", hidden)).StatusCode == HttpStatusCode.Created, "another entry remains pending");
        // Correct/reenter the same logical line. The old revision must never be approvable.
        var correction = hidden with { RequestId = Guid.NewGuid(), ReceiptId = hidden.RequestId, ReplacesRevisionId = hidden.RequestId, CorrectionReason = "Correct quantity", Pieces = 7 };
        Check((await Post(op, "/api/stock/receipts", correction)).StatusCode == HttpStatusCode.Created, "operator correction resubmitted");
        Check((await Post(admin, $"/api/stock/receipts/{hidden.RequestId}/review", new StockReceiving.ReviewInput(hidden.RequestId, true))).StatusCode == HttpStatusCode.Conflict, "stale exported revision cannot approve");
        Check((await Post(admin, $"/api/stock/receipts/{hidden.RequestId}/review", new StockReceiving.ReviewInput(correction.RequestId, false))).StatusCode == HttpStatusCode.BadRequest, "return requires reason");
        Check((await Post(admin, $"/api/stock/receipts/{hidden.RequestId}/review", new StockReceiving.ReviewInput(correction.RequestId, false, Note: "Check carton count"))).IsSuccessStatusCode, "admin returns line");
        var replacement = correction with { RequestId = Guid.NewGuid(), ReplacesRevisionId = correction.RequestId, CorrectionReason = "Admin checked cartons", Cartons = 1, VerifyMrp = true };
        Check((await Post(admin, "/api/stock/receipts", replacement)).StatusCode == HttpStatusCode.Created, "admin correction autoapproved");
        Check((await Post(admin, "/api/stock/receipts", replacement)).StatusCode == HttpStatusCode.OK, "replacement retry idempotent");
        Check((await Post(op, "/api/stock/receipts", replacement with { RequestId = Guid.NewGuid(), VerifyMrp = false })).StatusCode == HttpStatusCode.Forbidden, "operator cannot edit admin-owned revision");
        var history = await op.GetFromJsonAsync<JsonElement>($"/api/stock/receipts/{hidden.RequestId}");
        Check(history.GetArrayLength() == 3, "all corrections remain in history");
        Check(history[0].GetProperty("automaticApproval").GetBoolean(), "automatic approval recorded");
        var conflictBatch = own with { RequestId = Guid.NewGuid(), ExpiryDate = today.AddYears(2) };
        Check((await Post(admin, "/api/stock/receipts", conflictBatch)).StatusCode == HttpStatusCode.BadRequest, "existing batch date conflict blocked atomically");
        // Expired deliveries are recorded but cannot become sellable even with verified MRP.
        var expired = own with { RequestId = Guid.NewGuid(), BatchNumber = "EXPIRED-DELIVERY", ExpiryDate = today.AddDays(-1), Pieces = 9 };
        Check((await Post(admin, "/api/stock/receipts", expired)).StatusCode == HttpStatusCode.Created, "expired delivery recorded");
        inventory = await admin.GetFromJsonAsync<JsonElement>("/api/stock/inventory?availability=held");
        Check(inventory.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("lotId").GetGuid() == expired.RequestId && x.GetProperty("sellableUnits").GetInt64() == 0), "expired delivery held");
        var disposal = new StockDisposalEndpoints.DisposeInput(Guid.NewGuid(), expired.RequestId, "Disposed according to pharmacy procedure", true);
        Check((await Post(op, "/api/stock/disposals", disposal)).StatusCode == HttpStatusCode.Forbidden, "operator disposal blocked");
        Check((await admin.PostAsJsonAsync("/api/stock/disposals", disposal)).StatusCode == HttpStatusCode.BadRequest, "disposal CSRF enforced");
        Check((await Post(admin, "/api/stock/disposals", disposal with { PhysicallyDisposed = false })).StatusCode == HttpStatusCode.BadRequest, "physical disposal confirmation required");
        Check((await Post(admin, "/api/stock/disposals", disposal with { LotId = own.RequestId })).StatusCode == HttpStatusCode.BadRequest, "unexpired stock disposal blocked");
        var disposeResults = await Task.WhenAll(Post(admin, "/api/stock/disposals", disposal), Post(admin, "/api/stock/disposals", disposal));
        Check(disposeResults.All(x => x.IsSuccessStatusCode), "disposal retry idempotent");
        Check((await Post(admin, "/api/stock/disposals", disposal with { RequestId = Guid.NewGuid() })).StatusCode == HttpStatusCode.Conflict, "second disposal of lot blocked");
        inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory");
        Check(!inventory.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("lotId").GetGuid() == expired.RequestId), "disposed stock removed from inventory");
        var disposed = await admin.GetFromJsonAsync<JsonElement>("/api/stock/disposals");
        Check(disposed.GetProperty("items").GetArrayLength() == 1, "disposal history retained");
        Check((await op.GetAsync("/api/stock/disposals/export")).StatusCode == HttpStatusCode.Forbidden, "disposal CSV admin only");
        Check((await admin.GetStringAsync("/api/stock/disposals/export")).Contains("EXPIRED-DELIVERY"), "recent disposal exported");
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check(await db.ReceivingMovements.CountAsync(x => x.ReceiptId == input.RequestId) == 1, "exactly one receiving movement");
            Check(await db.ReceivingMovements.CountAsync(x => x.ReceiptId == hidden.RequestId) == 1, "correction posts once");
            Check((await db.StockReceiptRevisions.FindAsync(input.RequestId))!.MrpAmount == 600m, "older lot price preserved");
            Check(await db.StockReviewEvents.AnyAsync(x => x.RevisionId == correction.RequestId && x.Action == "Returned"), "return audit preserved");
            var record = await db.StockDisposals.SingleAsync();
            Check(record.Quantity == 9 && record.VisibleUntil == record.DisposedAt.AddMonths(3), "three calendar month history window");
            // Simulate current shelf stock crossing its expiry date; no receiving/edit action should be necessary.
            var batch = await db.MedicineBatches.SingleAsync(x => x.BatchNumber == "RECV-A");
            batch.ExpiryDate = today.AddDays(-1);
            record.DisposedAt = DateTimeOffset.UtcNow.AddMonths(-4); record.VisibleUntil = record.DisposedAt.AddMonths(3);
            await db.SaveChangesAsync();
        }
        inventory = await op.GetFromJsonAsync<JsonElement>("/api/stock/inventory?availability=available");
        Check(!inventory.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("lotId").GetGuid() == input.RequestId), "current expired shelf stock automatically unavailable");
        disposed = await admin.GetFromJsonAsync<JsonElement>("/api/stock/disposals");
        Check(disposed.GetProperty("items").GetArrayLength() == 0, "history hidden after three months");
        Check(!(await admin.GetStringAsync("/api/stock/disposals/export")).Contains("EXPIRED-DELIVERY"), "expired history excluded from export");
        Check(StockViews.CsvCell("  @formula").StartsWith("\"'"), "leading whitespace formula neutralized");
        Console.WriteLine("PASS: stock conversions, approval, price verification, corrections, stale review, CSV, expiry, disposal, retention and role permissions.");
    }
}
