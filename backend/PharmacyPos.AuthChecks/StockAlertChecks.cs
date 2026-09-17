using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Returns;
public static class StockAlertChecks
{
    static void Check(bool ok,string m){if(!ok)throw new Exception("STOCK ALERT CHECK FAILED: "+m);}
    static async Task<HttpResponseMessage> Post(HttpClient c,string url,object input) {
        var s=await c.GetFromJsonAsync<JsonElement>("/api/auth/session");var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};
        r.Headers.Add("X-CSRF-TOKEN",s.GetProperty("csrfToken").GetString());return await c.SendAsync(r);
    }
    public static async Task Run(HttpClient admin,HttpClient op,HttpClient anon,IServiceProvider services,Guid original) {
        Guid medicine;
        using(var scope=services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();var template=await db.Medicines.SingleAsync(x=>x.Id==original);var m=new Medicine{BrandName="Alert fixture",ManufacturerId=template.ManufacturerId,DosageFormId=template.DosageFormId,Classification=MedicineClassification.Otc};db.Medicines.Add(m);await db.SaveChangesAsync();medicine=m.Id;}
        var today=StockReceiving.ShopToday();
        async Task<Guid> Receive(int days,int pieces,bool verified=true,bool pending=false) {
            var r=new StockReceiving.ReceiveInput(Guid.NewGuid(),medicine,"Alert supplier","ALERT",today,"ALERT-"+days+"-"+pieces,today.AddDays(-10),today.AddDays(days),10,3,30,10,pieces,0,0,0,20,"Piece",verified);
            Check((await Post(pending?op:admin,"/api/stock/receipts",r)).IsSuccessStatusCode,"receiving fixture");return r.RequestId;
        }
        var now=await Receive(0,10);var edge=await Receive(90,20);var later=await Receive(91,30);await Receive(-1,40);await Receive(10,50,false);await Receive(20,60,false,true);
        const string low="/api/stock-alerts/low?search=Alert%20fixture";const string expiry="/api/stock-alerts/expiry?search=Alert%20fixture";
        async Task<JsonElement[]> Items(string url)=> (await op.GetFromJsonAsync<JsonElement>(url)).GetProperty("items").EnumerateArray().ToArray();
        foreach(var url in new[]{low,expiry})Check((await anon.GetAsync(url)).StatusCode==HttpStatusCode.Unauthorized,"anonymous read denied");
        Check((await op.GetAsync(low+"&all=true")).StatusCode==HttpStatusCode.Forbidden,"operator settings list denied");
        Check((await Items(low)).Length==0,"unset minimum does not silently alert");
        var input=new StockAlerts.Input(Guid.NewGuid(),Guid.Empty,60,"Initial minimum");var save=$"/api/stock-alerts/{medicine}/threshold";
        Check((await Post(op,save,input)).StatusCode==HttpStatusCode.Forbidden,"operator edit denied");
        Check((await admin.PostAsJsonAsync(save,input)).StatusCode==HttpStatusCode.BadRequest,"CSRF required");
        Check((await Post(admin,save,input with{Minimum=-1})).StatusCode==HttpStatusCode.BadRequest,"negative threshold denied");
        Check((await Post(admin,save,input)).IsSuccessStatusCode,"admin threshold");Check((await Post(admin,save,input)).IsSuccessStatusCode,"retry safe");
        var rows=await Items(low);Check(rows.Length==1&&rows[0].GetProperty("availableUnits").GetInt64()==60,"aggregate approved sellable stock excludes pending expired and unverified; equality alerts");
        Check(!rows[0].ToString().Contains("cost",StringComparison.OrdinalIgnoreCase),"no purchase costs in operator alerts");
        var near=await Items(expiry);Check(near.Length==3&&near.Any(x=>x.GetProperty("daysRemaining").GetInt32()==0)&&near.Any(x=>x.GetProperty("daysRemaining").GetInt32()==90),"inclusive today and day 90, excludes day 91 and expired/pending");
        Check(near.Single(x=>x.GetProperty("remainingUnits").GetInt64()==50).GetProperty("sellable").GetBoolean()==false,"approved unverified near-expiry shown as held");
        Check((await Post(admin,save,input with{RequestId=Guid.NewGuid(),Minimum=59})).StatusCode==HttpStatusCode.Conflict,"stale change rejected");
        var less=input with{RequestId=Guid.NewGuid(),ExpectedVersion=input.RequestId,Minimum=59};Check((await Post(admin,save,less)).IsSuccessStatusCode,"lower threshold");Check((await Items(low)).Length==0,"above minimum not low");
        var zero=less with{RequestId=Guid.NewGuid(),ExpectedVersion=less.RequestId,Minimum=0};await Post(admin,save,zero);
        var lines=new List<SalesCounter.CartLine>{new(now,10,"Piece"),new(edge,20,"Piece"),new(later,30,"Piece")};
        var quote=await(await Post(op,"/api/sales/quote",new SalesCounter.CartInput(lines))).Content.ReadFromJsonAsync<JsonElement>();var sale=new CashCheckout.Input(Guid.NewGuid(),lines,quote.GetProperty("quoteHash").GetString(),100000);
        Check((await Post(op,"/api/sales/checkout",sale)).IsSuccessStatusCode,"sell monitored stock");
        Check((await Items(low)).Single().GetProperty("availableUnits").GetInt64()==0,"out of stock alert updates from sale");Check((await Items(expiry)).Length==1,"sold-out batches absent from expiry list");
        var ret=new ReturnEndpoints.Input(Guid.NewGuid(),sale.RequestId,[0],"Alert return",true,true,true);var returned=await Post(op,"/api/returns",ret);Check(returned.IsSuccessStatusCode,"held return");
        var item=(await returned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();
        Check((await Items(low)).Single().GetProperty("availableUnits").GetInt64()==0,"held return not sellable");
        Check((await Post(admin,$"/api/returns/stock/{item}/review",new ReturnEndpoints.Review("Restocked","Checked",true))).IsSuccessStatusCode,"return approval");
        Check((await Items(low)).Length==0,"approved return clears shortage");Check((await Items(expiry)).Length==2,"approved return restores near-expiry quantity");
        var off=zero with{RequestId=Guid.NewGuid(),ExpectedVersion=zero.RequestId,Minimum=null,Reason="Disable monitoring"};Check((await Post(admin,save,off)).IsSuccessStatusCode,"monitoring disabled");
        var history=await admin.GetFromJsonAsync<JsonElement>($"/api/stock-alerts/{medicine}/history");Check(history.GetArrayLength()==4&&history.EnumerateArray().All(x=>x.GetProperty("actorName").GetString()=="admin-test"),"audit preserves changes and actor without duplicate retry");
        Check((await op.GetAsync($"/api/stock-alerts/{medicine}/history")).StatusCode==HttpStatusCode.Forbidden,"history admin only");
        Check((await op.GetAsync(low+"&page=0")).StatusCode==HttpStatusCode.BadRequest,"invalid page");
        Console.WriteLine("Stock alert checks passed: permissions, thresholds, expiry boundaries, sellable quantities, returns and audit.");
    }
}
