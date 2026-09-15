using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class ChargeChecks
{
    private static void Check(bool pass, string message) { if (!pass) throw new Exception("CHARGE CHECK FAILED: " + message); }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object payload) {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        return await client.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, HttpClient anonymous, IServiceProvider services, Guid medicineId, Guid otherMedicineId) {
        var today = StockReceiving.ShopToday();
        var receipt = new StockReceiving.ReceiveInput(Guid.NewGuid(), medicineId, "Charge supplier", "CHARGES", today, "CHARGES-A", today.AddDays(-1), today.AddDays(30), 10, 3, 30, 10, 600, 0, 0, 0, 600m, "Box", true);
        Check((await Post(admin, "/api/stock/receipts", receipt)).IsSuccessStatusCode, "stock fixture");
        var second = receipt with { RequestId = Guid.NewGuid(), BatchNumber = "CHARGES-B" };
        Check((await Post(admin, "/api/stock/receipts", second)).IsSuccessStatusCode, "second lot fixture");
        var other = receipt with { RequestId = Guid.NewGuid(), MedicineId = otherMedicineId, BatchNumber = "CHARGES-C", UnitsPerStrip = 0, StripsPerBox = 0, UnitsPerBox = 1, MrpAmount = 20m, MrpUnit = "Piece" };
        Check((await Post(admin, "/api/stock/receipts", other)).IsSuccessStatusCode, "other medicine fixture");
        var percentage = new ChargeRules.Input(Guid.NewGuid(), "Test percentage", "Percentage", 10m, true, [medicineId, medicineId]);
        Check((await anonymous.GetAsync("/api/charges")).StatusCode == HttpStatusCode.Unauthorized, "anonymous cannot list charge settings");
        Check((await op.GetAsync("/api/charges")).StatusCode == HttpStatusCode.Forbidden, "operator cannot list settings");
        Check((await Post(op, "/api/charges", percentage)).StatusCode == HttpStatusCode.Forbidden, "operator cannot configure charges");
        Check((await admin.PostAsJsonAsync("/api/charges", percentage)).StatusCode == HttpStatusCode.BadRequest, "create CSRF");
        Check((await Post(admin, "/api/charges", percentage with { Value = 101 })).StatusCode == HttpStatusCode.BadRequest, "percentage limit");
        Check((await Post(admin, "/api/charges", percentage with { Value = -1 })).StatusCode == HttpStatusCode.BadRequest, "negative rejected");
        Check((await Post(admin, "/api/charges", percentage with { Value = 1.001m })).StatusCode == HttpStatusCode.BadRequest, "excess precision rejected");
        Check((await Post(admin, "/api/charges", percentage with { AllMedicines = false, MedicineIds = [] })).StatusCode == HttpStatusCode.BadRequest, "empty selection rejected");
        Check((await Post(admin, "/api/charges", percentage with { MedicineIds = [Guid.NewGuid()] })).StatusCode == HttpStatusCode.BadRequest, "unknown medicine rejected");
        var created = await Task.WhenAll(Post(admin, "/api/charges", percentage), Post(admin, "/api/charges", percentage));
        Check(created.All(r => r.IsSuccessStatusCode), "concurrent identical retry");
        Check((await Post(admin, "/api/charges", percentage with { Value = 11 })).StatusCode == HttpStatusCode.Conflict, "changed retry rejected");
        Check((await Post(admin, "/api/charges", percentage with { RequestId = Guid.NewGuid(), Name = " test   PERCENTAGE " })).StatusCode == HttpStatusCode.Conflict, "duplicate active name rejected");
        var perUnit = new ChargeRules.Input(Guid.NewGuid(), "Test unit", "PerUnit", 2m, false, [medicineId]);
        var once = new ChargeRules.Input(Guid.NewGuid(), "Test once", "PerMedicine", 3m, true, []);
        Check((await Post(admin, "/api/charges", perUnit)).IsSuccessStatusCode, "selected per-unit charge");
        Check((await Post(admin, "/api/charges", once)).IsSuccessStatusCode, "global per-medicine charge");
        async Task<JsonElement> Quote(params SalesCounter.CartLine[] lines) {
            var response = await Post(op, "/api/sales/quote", new SalesCounter.CartInput(lines.ToList()));
            Check(response.IsSuccessStatusCode, "quote succeeded"); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var quote = await Quote(new SalesCounter.CartLine(receipt.RequestId, 1, "Strip"), new(second.RequestId, 2, "Piece"));
        Check(quote.GetProperty("subtotal").GetDecimal() == 240 && quote.GetProperty("chargeTotal").GetDecimal() == 51 && quote.GetProperty("estimatedTotal").GetInt64() == 291, "percentage + per-unit + once across packs and lots, global/selected overlap once");
        Check(quote.GetProperty("lines")[0].GetProperty("finalPrice").GetDecimal() == 242.5m && quote.GetProperty("lines")[1].GetProperty("finalPrice").GetDecimal() == 48.5m, "once charge proportionally allocated");
        var joined = await Quote(new SalesCounter.CartLine(receipt.RequestId, 12, "Piece"));
        Check(joined.GetProperty("estimatedTotal").GetInt64() == 291, "splitting packs does not change charges");
        var twoMedicines = await Quote(new SalesCounter.CartLine(receipt.RequestId, 1, "Piece"), new(other.RequestId, 1, "Piece"));
        Check(twoMedicines.GetProperty("chargeTotal").GetDecimal() == 12, "selected charge excluded from other medicine; once charged to each distinct medicine");
        Check((await Post(op, $"/api/charges/{once.RequestId}/stop", new { })).StatusCode == HttpStatusCode.Forbidden, "operator cannot stop charge");
        Check((await admin.PostAsJsonAsync($"/api/charges/{once.RequestId}/stop", new { })).StatusCode == HttpStatusCode.BadRequest, "stop CSRF");
        Check((await Post(admin, $"/api/charges/{once.RequestId}/stop", new { })).IsSuccessStatusCode, "admin stop");
        Check((await Post(admin, $"/api/charges/{once.RequestId}/stop", new { })).IsSuccessStatusCode, "idempotent stop");
        quote = await Quote(new SalesCounter.CartLine(receipt.RequestId, 12, "Piece"));
        Check(quote.GetProperty("chargeTotal").GetDecimal() == 48, "stopped charge excluded immediately");
        foreach (var id in new[] { perUnit.RequestId, percentage.RequestId }) Check((await Post(admin, $"/api/charges/{id}/stop", new { })).IsSuccessStatusCode, "stop remaining test rule");
        var replacement = once with { RequestId = Guid.NewGuid(), Value = 0.5m };
        Check((await Post(admin, "/api/charges", replacement)).IsSuccessStatusCode, "stopped name can be replaced");
        quote = await Quote(new SalesCounter.CartLine(receipt.RequestId, 1, "Piece"));
        Check(quote.GetProperty("estimatedTotal").GetInt64() == 21 && quote.GetProperty("roundingAdjustment").GetDecimal() == .5m, "round after charges, half up");
        await Post(admin, $"/api/charges/{replacement.RequestId}/stop", new { });
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/charges");
        Check(list.GetArrayLength() == 4, "one creation per retry and stopped rules retained");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        Check((await db.ReceivingMovements.SingleAsync(x => x.ReceiptId == receipt.RequestId)).Quantity == 600, "charge quote does not deduct stock");
        Check(await db.ChargeRules.AllAsync(x => x.CreatedBy != "" && x.StoppedBy != null && x.StoppedAt != null), "admin audit attribution preserved");
        Console.WriteLine("PASS: charge authorization, CSRF, validation, retries, scopes, pack/lot invariance, allocations, stops, rounding and audit history.");
    }
}
