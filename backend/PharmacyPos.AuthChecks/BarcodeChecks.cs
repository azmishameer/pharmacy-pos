using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;

public static class BarcodeChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("BARCODE CHECK FAILED: " + message); }
    static async Task<HttpResponseMessage> Post(HttpClient client, string url, object input) {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()); return await client.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, IServiceProvider services, Guid medicineId) {
        var input = new Barcodes.Input(medicineId, "0012345678905", "Box", 30);
        Check((await Post(op,"/api/barcodes",input)).StatusCode == HttpStatusCode.Forbidden,"operator cannot assign codes");
        Check((await admin.PostAsJsonAsync("/api/barcodes",input)).StatusCode == HttpStatusCode.BadRequest,"CSRF required");
        foreach (var bad in new[] {input with {Code=""}, input with {Code="A\nB"}, input with {Units=0},input with {Unit="Carton"},input with {Unit="Piece",Units=30}})
            Check((await Post(admin,"/api/barcodes",bad)).StatusCode==HttpStatusCode.BadRequest,"invalid mapping rejected");
        var concurrent = await Task.WhenAll(Post(admin,"/api/barcodes",input),Post(admin,"/api/barcodes",input));
        Check(concurrent.All(r=>r.IsSuccessStatusCode),"concurrent registration idempotent");
        Check((await Post(admin,"/api/barcodes",input with {Units=10})).StatusCode==HttpStatusCode.Conflict,"duplicate cannot change pack");
        var lookup=await op.GetFromJsonAsync<JsonElement>("/api/barcodes/lookup?code=0012345678905");
        Check(lookup.GetProperty("code").GetString()==input.Code && lookup.GetProperty("unit").GetString()=="Box", "leading zeros and pack preserved");
        Check((await op.GetAsync("/api/barcodes/lookup?code=12345678905")).StatusCode==HttpStatusCode.NotFound,"exact matching");
        var stock=await op.GetFromJsonAsync<JsonElement>("/api/sales/stock?barcode=0012345678905");
        Check(stock.GetProperty("items").GetArrayLength()>0,"sellable matching batches found");
        foreach(var row in stock.GetProperty("items").EnumerateArray()) Check(row.GetProperty("medicineId").GetGuid()==medicineId && row.GetProperty("unitsPerBox").GetInt32()==30 && row.GetProperty("availableUnits").GetInt64()>0,"only matching pack and medicine");
        Check((await Post(admin,"/api/barcodes",input with {Code="PACK-MISMATCH",Units=999999})).IsSuccessStatusCode,"second pack");
        var empty=await op.GetFromJsonAsync<JsonElement>("/api/sales/stock?barcode=PACK-MISMATCH");
        Check(empty.GetProperty("items").GetArrayLength()==0,"incompatible packs excluded");
        var id=lookup.GetProperty("id").GetGuid();
        Check((await Post(op,$"/api/barcodes/{id}/disable",new{})).StatusCode==HttpStatusCode.Forbidden,"operator cannot retire");
        Check((await Post(admin,$"/api/barcodes/{id}/disable",new{})).IsSuccessStatusCode,"admin retire");
        Check((await op.GetAsync("/api/barcodes/lookup?code=0012345678905")).StatusCode==HttpStatusCode.NotFound,"retired cannot scan");
        Check((await op.GetAsync("/api/sales/stock?barcode=0012345678905")).StatusCode==HttpStatusCode.NotFound,"retired cannot filter sale");
        Check((await Post(admin,"/api/barcodes",input)).StatusCode==HttpStatusCode.Conflict,"retired cannot be silently reused");
        using var scope=services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        var saved=await db.Set<MedicineBarcode>().SingleAsync(b=>b.Code==input.Code);
        Check(saved.DisabledAt!=null && saved.DisabledBy!=null && saved.CreatedBy.Length>0,"audit retained");
        // A usable mapping remains for the isolated browser scanner checks.
        Check((await Post(admin,"/api/barcodes",input with {Code="0012345678912"})).IsSuccessStatusCode,"browser fixture mapping");
        Console.WriteLine("Barcode checks passed: permissions, CSRF, validation, exact codes, concurrency, pack filtering, retirement and audit.");
    }
}
