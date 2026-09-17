using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Returns;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Stock;

public static class PaymentChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception("PAYMENT CHECK FAILED: " + message); }
    static async Task<HttpResponseMessage> Post(HttpClient client, string url, object input) {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post,url) { Content=JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString()); return await client.SendAsync(request);
    }
    public static async Task Run(HttpClient admin, HttpClient op, IServiceProvider services, Guid medicineId) {
        foreach(var x in (await admin.GetFromJsonAsync<JsonElement>("/api/offers")).EnumerateArray()) await Post(admin,$"/api/offers/{x.GetProperty("id").GetGuid()}/stop",new{});
        foreach(var x in (await admin.GetFromJsonAsync<JsonElement>("/api/charges")).EnumerateArray()) await Post(admin,$"/api/charges/{x.GetProperty("id").GetGuid()}/stop",new{});
        var today=StockReceiving.ShopToday();
        var stock=new StockReceiving.ReceiveInput(Guid.NewGuid(),medicineId,"Payment supplier","PAYMENTS",today,"PAYMENTS-A",today.AddDays(-1),today.AddDays(30),10,3,30,10,100,0,0,0,600,"Box",true);
        Check((await Post(admin,"/api/stock/receipts",stock)).IsSuccessStatusCode,"stock fixture");
        var lines=new List<SalesCounter.CartLine>{new(stock.RequestId,1,"Piece"),new(stock.RequestId,2,"Piece")};
        async Task<CashCheckout.Input> Request(List<PaymentMethods.Input> payments) {
            var q=await (await Post(op,"/api/sales/quote",new SalesCounter.CartInput(lines))).Content.ReadFromJsonAsync<JsonElement>();
            return new(Guid.NewGuid(),lines,q.GetProperty("quoteHash").GetString(),0,payments);
        }
        var payments=new List<PaymentMethods.Input>{new("Cash",10,20,null,true),new("Card",20,null,"CARD-TEST-001",true),new("bKash",15,null,"BKASH-TEST-001",true),new("Nagad",15,null,"NAGAD-TEST-001",true)};
        var input=await Request(payments);
        async Task Invalid(List<PaymentMethods.Input> bad,string name) => Check((await Post(op,"/api/sales/checkout",input with {Payments=bad})).StatusCode==HttpStatusCode.BadRequest,name);
        await Invalid([new("Cash",59,100,null,true)],"underpayment rejected");
        await Invalid([new("Cash",61,100,null,true)],"over allocation rejected");
        await Invalid([new("Other",60,null,"OTHER",true)],"unknown method rejected");
        await Invalid([new("Cash",30,30,null,true),new("Cash",30,30,null,true)],"repeated method rejected");
        await Invalid([new("Card",60,null,null,true)],"noncash reference required");
        await Invalid([new("Card",60,null,"CARD-TEST-002",false)],"payment verification required");
        await Invalid([new("Card",60,61,"CARD-TEST-002",true)],"noncash change rejected");
        await Invalid([new("Cash",60,59,null,true)],"cash must cover its portion");
        await Invalid([new("Cash",59.999m,100,null,true)],"fractional paisa rejected");
        SalesReports.Report before;
        using(var scope=services.CreateScope()) before=await SalesReports.Build(scope.ServiceProvider.GetRequiredService<PharmacyDbContext>(),today,today,default);
        var race=await Task.WhenAll(Post(op,"/api/sales/checkout",input),Post(op,"/api/sales/checkout",input));
        Check(race.All(r=>r.IsSuccessStatusCode),"split payment retry");
        var receipt=await race[0].Content.ReadFromJsonAsync<JsonElement>();
        Check(receipt.GetProperty("payments").GetArrayLength()==4,"four methods saved");
        Check(receipt.GetProperty("payments").EnumerateArray().Sum(p=>p.GetProperty("amount").GetDecimal())==60,"allocated sum exact");
        Check(receipt.GetProperty("payments")[0].GetProperty("change").GetDecimal()==10,"cash-only change");
        Check(receipt.GetProperty("payments")[1].GetProperty("reference").GetString()=="CARD-TEST-001","reference on receipt");
        var duplicate=await Request(payments.Select(p=>p with {Reference=p.Reference?.ToLowerInvariant()}).ToList());
        var denied=await Post(op,"/api/sales/checkout",duplicate);
        Check(denied.StatusCode==HttpStatusCode.Conflict && (await denied.Content.ReadAsStringAsync()).Contains("already recorded"),"reference cannot be reused for another sale");
        var number=receipt.GetProperty("receiptNumber").GetString();
        var lookup=await op.GetFromJsonAsync<JsonElement>($"/api/returns/sale?number={number}");
        Check(lookup.GetProperty("lines")[0].GetProperty("originalPayments").EnumerateArray().Sum(p=>p.GetProperty("amount").GetDecimal())==20,"refund preview sums to line total");
        var cashReturn=new ReturnEndpoints.Input(Guid.NewGuid(),input.RequestId,[1],"Cash instead of original",true,true,true,"Cash");
        var cashResult=await Post(op,"/api/returns",cashReturn);Check(cashResult.IsSuccessStatusCode,"operator may refund noncash sale in cash");
        Check((await cashResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("payments")[0].GetProperty("amount").GetDecimal()==40,"entire selected line refunded in cash");
        var originalReturn=new ReturnEndpoints.Input(Guid.NewGuid(),input.RequestId,[0],"Original methods",true,true,true,"Original",[new("Card","CARD-REFUND-001"),new("bKash","BKASH-REFUND-001"),new("Nagad","NAGAD-REFUND-001")]);
        Check((await Post(admin,"/api/returns",originalReturn with {PaymentReferences=null})).StatusCode==HttpStatusCode.BadRequest,"noncash refund reference required");
        var originalResult=await Post(admin,"/api/returns",originalReturn);Check(originalResult.IsSuccessStatusCode,"original split refund");
        Check((await Post(admin,"/api/returns",originalReturn)).IsSuccessStatusCode,"refund retry safe");
        Check((await Post(admin,"/api/returns",originalReturn with {PaymentReferences=[new("Card","changed"),new("bKash","BKASH-REFUND-001"),new("Nagad","NAGAD-REFUND-001")]})).StatusCode==HttpStatusCode.Conflict,"changed refund reference rejected on retry");
        using(var scope=services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
            Check(await db.Set<SalePayment>().CountAsync(p=>p.SaleId==input.RequestId)==4,"payment rows not duplicated");
            Check(await db.SaleStockMovements.Where(m=>m.SaleId==input.RequestId).SumAsync(m=>m.Quantity)==3,"stock deducted once");
            Check(await db.SaleReturns.Where(r=>r.SaleId==input.RequestId).SumAsync(r=>r.Amount)==60,"complete refunds total original bill");
            Check(await db.ReturnedItems.Where(r=>r.SaleId==input.RequestId && r.Status=="Held").SumAsync(r=>r.Quantity)==3,"refund stock remains held");
            var report=await SalesReports.Build(db,today,today,default);
            foreach(var (method,collected,refunded) in new[]{("Cash",10m,43.33m),("Card",20m,6.67m),("bKash",15m,5m),("Nagad",15m,5m)}) {
                var a=report.Totals.Payments.Single(p=>p.Method==method);var b=before.Totals.Payments.Single(p=>p.Method==method);
                Check(a.Collected-b.Collected==collected && a.Refunded-b.Refunded==refunded,"accurate report for "+method);
            }
            Check(report.Totals.CashCollected-before.Totals.CashCollected==10,"noncash excluded from cash collected");
            Check(SalesReports.Csv(report).Contains("bKash collected"),"CSV payment columns");
        }
        foreach(var method in new[]{"Card","bKash","Nagad"}) {
            var single=await Request([new(method,60,null,method+"-SINGLE",true)]);
            Check((await Post(op,"/api/sales/checkout",single)).IsSuccessStatusCode,method+" single payment");
        }
        var tiny=new Sale {Total=.05m,Snapshot="{\"lines\":[{\"finalPrice\":0.01},{\"finalPrice\":0.02},{\"finalPrice\":0.02}]}",Payments=[new(){Method="Cash",Amount=.01m},new(){Method="Card",Amount=.02m},new(){Method="bKash",Amount=.02m}]};
        var shares=RefundPayments.For(tiny);
        Check(shares.Select(x=>x.Sum(p=>p.Amount)).SequenceEqual(new[]{.01m,.02m,.02m}),"paisa allocation line totals");
        foreach(var payment in tiny.Payments) Check(shares.SelectMany(x=>x).Where(p=>p.Method==payment.Method).Sum(p=>p.Amount)==payment.Amount,"paisa allocation payment totals");
        Console.WriteLine("Payment checks passed: four methods, split totals, cash change, references, duplicate prevention, retry safety, refund destinations, exact allocation, reports and held stock.");
    }
}
