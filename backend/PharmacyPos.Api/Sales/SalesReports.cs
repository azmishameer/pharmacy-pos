using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Sales;

public static class SalesReports
{
    public sealed record PaymentTotal(string Method, decimal Collected, decimal Refunded) { public decimal Net => Collected - Refunded; }
    private static Totals AddPayment(Totals t, string method, decimal collected, decimal refunded) => t with {
        Payments = PaymentMethods.All.Select(m => { var prior = t.Payments.FirstOrDefault(p => p.Method == m); return new PaymentTotal(m, (prior?.Collected ?? 0) + (m == method ? collected : 0), (prior?.Refunded ?? 0) + (m == method ? refunded : 0)); }).ToArray()
    };
    public sealed record Totals(int SalesCount, int RefundCount, decimal Subtotal, decimal Discounts, decimal Charges,
        decimal Rounding, decimal Sales, decimal Refunds, decimal CashCollected, decimal CashRefunded)
    {
        public PaymentTotal[] Payments { get; init; } = [];
        public decimal NetSales => Sales - Refunds;
        public decimal NetCash => CashCollected - CashRefunded;
    }
    public sealed record StaffRow(string StaffId, string Username, Totals Totals);
    public sealed record Report(DateOnly From, DateOnly To, string TimeZone, DateTimeOffset GeneratedAt, Totals Totals, List<StaffRow> Staff);
    public static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(6)).ToUniversalTime(),
         new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(6)).ToUniversalTime());

    public static async Task<Report> Build(PharmacyDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start,end)=Bounds(from,to);
        // Both queries see the same completed transactions, even during checkout/refund activity.
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var sales=await db.Sales.AsNoTracking().Where(s=>s.CompletedAt>=start&&s.CompletedAt<end)
            .Select(s=>new {s.OperatorId,s.OperatorName,s.Snapshot,s.Total,Payments=s.Payments.Select(p=>new {p.Method,p.Amount}).ToList()})
            .Take(50001).ToListAsync(ct);
        var refunds=await db.SaleReturns.AsNoTracking().Where(r=>r.At>=start&&r.At<end)
            .Select(r=>new {r.ActorId,r.ActorName,r.Amount,r.Method,Payments=r.Payments.Select(p=>new {p.Method,p.Amount}).ToList()}).Take(50001).ToListAsync(ct);
        if(sales.Count>50000||refunds.Count>50000)throw new InvalidOperationException("This period has too many transactions. Select a shorter date range.");
        var rows=new Dictionary<string,StaffRow>();
        var zero=new Totals(0,0,0,0,0,0,0,0,0,0);
        foreach(var s in sales) {
            var row=rows.GetValueOrDefault(s.OperatorId)??new StaffRow(s.OperatorId,s.OperatorName,zero);
            using var json=JsonDocument.Parse(s.Snapshot); var p=json.RootElement;var t=row.Totals;
            rows[s.OperatorId]=row with {Totals=t with {SalesCount=t.SalesCount+1,Subtotal=t.Subtotal+p.GetProperty("subtotal").GetDecimal(),
                Discounts=t.Discounts+p.GetProperty("discountTotal").GetDecimal(),Charges=t.Charges+p.GetProperty("chargeTotal").GetDecimal(),
                Rounding=t.Rounding+p.GetProperty("roundingAdjustment").GetDecimal(),Sales=t.Sales+s.Total,CashCollected=t.CashCollected+s.Payments.Where(p=>p.Method=="Cash").Sum(p=>p.Amount)}};
            foreach(var payment in s.Payments) rows[s.OperatorId] = rows[s.OperatorId] with { Totals = AddPayment(rows[s.OperatorId].Totals, payment.Method, payment.Amount, 0) };
        }
        foreach(var r in refunds) {
            var row=rows.GetValueOrDefault(r.ActorId)??new StaffRow(r.ActorId,r.ActorName,zero);var t=row.Totals;
            rows[r.ActorId]=row with {Totals=t with {RefundCount=t.RefundCount+1,Refunds=t.Refunds+r.Amount,CashRefunded=t.CashRefunded+(r.Payments.Count==0 ? (r.Method=="Cash"?r.Amount:0) : r.Payments.Where(p=>p.Method=="Cash").Sum(p=>p.Amount))}};
            if(r.Payments.Count==0) rows[r.ActorId] = rows[r.ActorId] with { Totals = AddPayment(rows[r.ActorId].Totals,r.Method,0,r.Amount) };
            else foreach(var payment in r.Payments) rows[r.ActorId] = rows[r.ActorId] with { Totals = AddPayment(rows[r.ActorId].Totals,payment.Method,0,payment.Amount) };
        }
        var values=rows.Values.Select(r=>r.Totals).ToArray();
        var total=new Totals(values.Sum(t=>t.SalesCount),values.Sum(t=>t.RefundCount),values.Sum(t=>t.Subtotal),values.Sum(t=>t.Discounts),
            values.Sum(t=>t.Charges),values.Sum(t=>t.Rounding),values.Sum(t=>t.Sales),values.Sum(t=>t.Refunds),values.Sum(t=>t.CashCollected),values.Sum(t=>t.CashRefunded));
        total = total with { Payments = PaymentMethods.All.Select(m => new PaymentTotal(m, values.Sum(t=>t.Payments.Where(p=>p.Method==m).Sum(p=>p.Collected)), values.Sum(t=>t.Payments.Where(p=>p.Method==m).Sum(p=>p.Refunded)))).ToArray() };
        await tx.CommitAsync(ct);
        return new(from,to,"Asia/Dhaka",DateTimeOffset.UtcNow,total,rows.Values.OrderBy(r=>r.Username).ThenBy(r=>r.StaffId).ToList());
    }
    static string Cell(string s) => "\""+((s.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')?"'":"")+s.Replace("\"","\"\"")+"\"";
    public static string Csv(Report r) {
        var b=new StringBuilder("From,To,Time zone,Generated at UTC,Staff ID,Staff,Sales count,Refund count,MRP subtotal,Discounts,Charges,Rounding,Sales,Refunds,Net sales,Cash collected,Cash refunded,Net cash,Card collected,Card refunded,Card net,bKash collected,bKash refunded,bKash net,Nagad collected,Nagad refunded,Nagad net\r\n");
        void Row(string id,string name,Totals t) {
            b.Append(string.Join(",",new[]{Cell(r.From.ToString("yyyy-MM-dd")),Cell(r.To.ToString("yyyy-MM-dd")),Cell(r.TimeZone),Cell(r.GeneratedAt.ToString("O")),Cell(id),Cell(name)}));
            foreach(var n in new decimal[]{t.SalesCount,t.RefundCount,t.Subtotal,t.Discounts,t.Charges,t.Rounding,t.Sales,t.Refunds,t.NetSales,t.CashCollected,t.CashRefunded,t.NetCash}) b.Append(',').Append(n.ToString(CultureInfo.InvariantCulture));
            foreach(var method in PaymentMethods.All.Where(m=>m!="Cash")) {
                var payment=t.Payments.FirstOrDefault(p=>p.Method==method) ?? new PaymentTotal(method,0,0);
                foreach(var amount in new[]{payment.Collected,payment.Refunded,payment.Net}) b.Append(',').Append(amount.ToString(CultureInfo.InvariantCulture));
            }
            b.Append("\r\n");
        }
        Row("","TOTAL",r.Totals);foreach(var s in r.Staff)Row(s.StaffId,s.Username,s.Totals);return b.ToString();
    }
    public static void MapSalesReports(this WebApplication app) {

        app.MapGet("/api/reports/sales",async(string? from,string? to,string? format,PharmacyDbContext db,HttpContext http,CancellationToken ct)=>{
            http.Response.Headers.CacheControl="no-store";
            var first=StockReceiving.ShopToday();var last=first;
            if((from!=null&&!DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first))||
               (to!=null&&!DateOnly.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last))||
               first.Year<2000||last.Year>9998||last<first||last.DayNumber-first.DayNumber>365||format is not(null or "csv"))
                return Results.BadRequest(new {message="Select a valid date range of at most 366 days."});
            try {
                var report=await Build(db,first,last,ct);
                return format=="csv"?Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(report))).ToArray(),"text/csv; charset=utf-8",$"sales-{first:yyyy-MM-dd}-{last:yyyy-MM-dd}.csv"):Results.Ok(report);
            }catch(InvalidOperationException e) when(e.Message.StartsWith("This period")) {return Results.BadRequest(new{message=e.Message});}
        }).RequireAuthorization("AdminOnly");
    }
}
