using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Returns;
using PharmacyPos.Api.Stock;
namespace PharmacyPos.Api.Sales;

public static class ProfitReports
{
    public sealed record Issue(string Kind, Guid Id, string Description);
    public sealed record Report(DateOnly From, DateOnly To, DateTimeOffset GeneratedAt, string TimeZone,
        int SalesCount, int ReturnedLines, int RestockedLines, decimal SalesPaid, decimal RefundsPaid,
        decimal? ExcludedSalesCharges, decimal? ExcludedRefundCharges, decimal? NetIncome,
        decimal? CostOfSales, decimal? RecoveredCost, decimal? NetCost, decimal? GrossProfit, List<Issue> Issues)
    { public bool Complete => Issues.Count == 0; }
    static decimal Round(decimal v)=>decimal.Round(v,6,MidpointRounding.AwayFromZero);
    // Allocate paid receipt rounding using the same immutable line shares as refunds.
    public static decimal ExcludedPart(JsonElement line, decimal paid, IReadOnlyDictionary<Guid,bool?> classifications, IDictionary<string,Issue> issues) {
        if(paid==0)return 0;
        decimal excluded=0;
        foreach(var charge in line.GetProperty("charges").EnumerateArray()) {
            var amount=charge.GetProperty("amount").GetDecimal();if(amount==0)continue;
            var id=charge.GetProperty("id").GetGuid();
            if(!classifications.TryGetValue(id,out var flag)||flag==null)issues["charge:"+id]=new("Charge",id,charge.GetProperty("name").GetString()??id.ToString());
            else if(flag.Value)excluded+=amount;
        }
        var final=line.GetProperty("finalPrice").GetDecimal();
        if(final<=0)throw new InvalidOperationException("Receipt pricing is inconsistent.");
        return paid*Math.Min(excluded,final)/final;
    }
    public static async Task<Report> Build(PharmacyDbContext db,DateOnly from,DateOnly to,CancellationToken ct) {
        var(start,end)=SalesReports.Bounds(from,to);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var sales=await db.Sales.AsNoTracking().Where(s=>s.CompletedAt>=start&&s.CompletedAt<end).OrderBy(s=>s.CompletedAt).ThenBy(s=>s.Id).Take(50001).ToListAsync(ct);
        var refunds=await db.ReturnedItems.AsNoTracking().Where(i=>i.Return.At>=start&&i.Return.At<end).OrderBy(i=>i.Return.At).ThenBy(i=>i.Id).Take(50001).ToListAsync(ct);
        var restored=await db.ReturnedItems.AsNoTracking().Where(i=>i.Status=="Restocked"&&i.ReviewedAt>=start&&i.ReviewedAt<end).OrderBy(i=>i.ReviewedAt).ThenBy(i=>i.Id).Take(50001).ToListAsync(ct);
        if(sales.Count>50000||refunds.Count>50000||restored.Count>50000)throw new ArgumentException("Select a shorter period: more than 50,000 sales or return items were found.");
        var refundSaleIds=refunds.Select(i=>i.SaleId).Except(sales.Select(s=>s.Id)).Distinct().ToArray();
        var originals=await db.Sales.AsNoTracking().Where(s=>refundSaleIds.Contains(s.Id)).ToListAsync(ct);
        var allSales=sales.Concat(originals).ToDictionary(s=>s.Id);
        var classifications=await db.ChargeRules.AsNoTracking().ToDictionaryAsync(c=>c.Id,c=>c.ExcludeFromProfit,ct);
        var issues=new Dictionary<string,Issue>();
        var parts=new Dictionary<Guid,decimal[]>();var saleIssues=new Dictionary<Guid,List<Issue>[]>();
        foreach(var sale in allSales.Values) {
            using var doc=JsonDocument.Parse(sale.Snapshot);var lines=doc.RootElement.GetProperty("lines").EnumerateArray().ToArray();var paid=RefundAllocation.For(sale);
            var excluded=new decimal[lines.Length];var unknown=new List<Issue>[lines.Length];
            for(var i=0;i<lines.Length;i++) { var local=new Dictionary<string,Issue>();excluded[i]=ExcludedPart(lines[i],paid[i],classifications,local);unknown[i]=local.Values.ToList(); }
            parts[sale.Id]=excluded;saleIssues[sale.Id]=unknown;
        }
        void AddIssues(IEnumerable<Issue> values) {foreach(var i in values)issues[i.Kind+":"+i.Id]=i;}
        decimal excludedSales=0,excludedRefunds=0;
        foreach(var s in sales){excludedSales+=parts[s.Id].Sum();AddIssues(saleIssues[s.Id].SelectMany(x=>x));}
        foreach(var r in refunds){excludedRefunds+=parts[r.SaleId][r.LineIndex];AddIssues(saleIssues[r.SaleId][r.LineIndex]);}
        var incomeComplete=issues.Count==0;
        var saleIds=sales.Select(s=>s.Id).ToArray();
        var sold=await db.SaleStockMovements.AsNoTracking().Where(m=>saleIds.Contains(m.SaleId)).GroupBy(m=>m.ReceiptId).Select(g=>new{Id=g.Key,Units=g.Sum(m=>m.Quantity)}).ToListAsync(ct);
        var recovered=restored.GroupBy(i=>i.ReceiptId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Quantity));
        var lots=sold.Select(m=>m.Id).Concat(recovered.Keys).Distinct().ToArray();
        var costs=await db.PurchaseCostEntries.AsNoTracking().Where(e=>lots.Contains(e.ReceiptId)&&!db.PurchaseCostEntries.Any(n=>n.ReceiptId==e.ReceiptId&&n.Revision>e.Revision)).ToDictionaryAsync(e=>e.ReceiptId,ct);
        var names=await db.ReceivingMovements.AsNoTracking().Where(m=>lots.Contains(m.ReceiptId)).Select(m=>new{m.ReceiptId,Name=m.Revision.Medicine.BrandName+" · Batch "+m.Batch.BatchNumber}).ToDictionaryAsync(m=>m.ReceiptId,m=>m.Name,ct);
        decimal? Cost(IEnumerable<(Guid Id,long Units)> movements) {
            decimal total=0;var complete=true;
            foreach(var m in movements) {
                if(!costs.TryGetValue(m.Id,out var c)){complete=false;issues["cost:"+m.Id]=new("Purchase cost",m.Id,names.GetValueOrDefault(m.Id,m.Id.ToString()));}
                else total+=c.TotalCost*m.Units/c.ReceivedUnits;
            }
            return complete?total:null;
        }
        var cost=Cost(sold.Select(m=>(m.Id,m.Units)));var recovery=Cost(recovered.Select(m=>(m.Key,m.Value)));
        var paidSales=sales.Sum(s=>s.Total);var paidRefunds=refunds.Sum(i=>i.Refund);
        decimal? income=incomeComplete?paidSales-excludedSales-paidRefunds+excludedRefunds:null;
        var netCost=cost-recovery;var profit=income-netCost;
        decimal? Display(decimal? v)=>v.HasValue?Round(v.Value):null;
        await tx.CommitAsync(ct);
        return new(from,to,DateTimeOffset.UtcNow,"Asia/Dhaka",sales.Count,refunds.Count,restored.Count,paidSales,paidRefunds,
            incomeComplete?Round(excludedSales):null,incomeComplete?Round(excludedRefunds):null,Display(income),Display(cost),Display(recovery),Display(netCost),Display(profit),issues.Values.OrderBy(i=>i.Kind).ThenBy(i=>i.Description).ToList());
    }
    public static string Csv(Report r) {
        var b=new StringBuilder("Section,Name,Value,Reference\r\n");
        void Row(string section,string name,object? value,object? id=null)=>b.Append(string.Join(",",new[]{(object)section,name,value,id}.Select(StockViews.CsvCell))).Append("\r\n");
        Row("Period","From",r.From);Row("Period","To",r.To);Row("Period","Time zone",r.TimeZone);Row("Period","Generated at",r.GeneratedAt);
        Row("Status","Profit status",r.Complete?"Complete":"Incomplete");
        foreach(var p in new (string,object?)[]{("Sales count",r.SalesCount),("Returned lines",r.ReturnedLines),("Restocked lines",r.RestockedLines),("Sales paid",r.SalesPaid),("Refunds paid",r.RefundsPaid),("Excluded sales charges",r.ExcludedSalesCharges),("Excluded refund charges",r.ExcludedRefundCharges),("Net income",r.NetIncome),("Cost of sales",r.CostOfSales),("Recovered cost",r.RecoveredCost),("Net cost",r.NetCost),("Gross profit",r.GrossProfit)})Row("Totals",p.Item1,p.Item2);
        foreach(var i in r.Issues)Row("Needs review",i.Kind,i.Description,i.Id);
        Row("Basis","Purchase costs","Latest corrected delivery costs; missing values are blank, not zero");
        Row("Basis","Timing","Sales and refunds by transaction date; recovered cost by resale approval date");
        Row("Basis","Excluded charges","Latest classification; receipt rounding allocated proportionally");
        Row("Basis","Scope","Before shop expenses; inventory write-offs outside sold/returned stock are not included");
        return b.ToString();
    }
    public static void MapProfitReports(this WebApplication app) {
        if(!app.Environment.IsDevelopment())return;
        app.MapGet("/api/reports/profit",async(string? from,string? to,string? format,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";var first=StockReceiving.ShopToday();var last=first;
            if((from!=null&&!DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first))||
               (to!=null&&!DateOnly.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last))||first.Year<2000||last.Year>9998||last<first||last.DayNumber-first.DayNumber>365||format is not(null or "csv"))
                return Results.BadRequest(new{message="Select a valid date range of at most 366 days."});
            try {var r=await Build(db,first,last,ct);return format=="csv"?Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(r))).ToArray(),"text/csv; charset=utf-8",$"profit-{first:yyyy-MM-dd}-{last:yyyy-MM-dd}.csv"):Results.Ok(r);}
            catch(ArgumentException e){return Results.BadRequest(new{message=e.Message});}
        }).RequireAuthorization("AdminOnly");
    }
}
