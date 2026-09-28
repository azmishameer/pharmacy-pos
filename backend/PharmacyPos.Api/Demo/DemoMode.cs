using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Data;
using PharmacyPos.Api.Stock;

namespace PharmacyPos.Api.Demo;

public static class DemoMode
{
    public static bool Enabled(IHostEnvironment env) => env.IsEnvironment("Demo");
    public static void Validate(IConfiguration config) {
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Pharmacy"));
        if (connection.Database != "pharmacy_pos_demo" || connection.Username != "pharmacy_demo")
            throw new InvalidOperationException("Demo mode requires its own pharmacy_pos_demo database and pharmacy_demo account. Real installation settings are not allowed.");
    }
    public static async Task Initialize(IServiceProvider services) {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public'";
        var tables = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        if (tables > 0) {
            cmd.CommandText = "SELECT to_regclass('public.pharmacy_demo_marker') IS NOT NULL";
            if (!(bool)(await cmd.ExecuteScalarAsync())!) throw new InvalidOperationException("Refusing to enable password-free demo access on an existing unmarked database. Use a new, empty demo database.");
            await db.Database.MigrateAsync(); return;
        }
        await db.Database.MigrateAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var role in new[] {"Admin", "Manager", "Operator"}) Ensure(await roles.CreateAsync(new(role)));
        var admin = new IdentityUser("demo-admin") { Id = "demo-admin" };
        var op = new IdentityUser("demo-operator") { Id = "demo-operator" };
        foreach (var user in new[] {admin, op}) {
            Ensure(await users.CreateAsync(user));
            Ensure(await users.AddToRoleAsync(user, user == admin ? "Admin" : "Operator"));
        }
        var manufacturer = new Manufacturer {Name="Demo Pharmaceuticals — fictional"};
        var form = new DosageForm {Name="Tablet"};
        db.AddRange(manufacturer,form);
        var today = StockReceiving.ShopToday();
        for (var i=0;i<4;i++) {
            var medicine = new Medicine {BrandName=new[]{"Demo Relief 500", "Demo Glucose 5/500", "Demo Allergy 10", "Demo Vitamin 100"}[i], Manufacturer=manufacturer,DosageForm=form,Classification=i==1?MedicineClassification.Prescription:MedicineClassification.Otc,BaseUnit=StockUnit.Tablet,CreatedByUserId=admin.Id,CreatedAt=DateTimeOffset.UtcNow};
            var receipt = new StockReceipt {Id=Guid.NewGuid(),CurrentRevision=1};
            var expiry=today.AddDays(i==2?30:i==3?-1:365);
            var revision = new StockReceiptRevision {Id=receipt.Id,Receipt=receipt,Revision=1,RequestHash=new string('0',64),Medicine=medicine,Supplier="Fictional Demo Supplier",ReceivedDate=today,BatchNumber=$"DEMO-{i+1:000}",ManufacturingDate=today.AddMonths(-6),ExpiryDate=expiry,BaseUnit=StockUnit.Tablet,UnitsPerStrip=10,StripsPerBox=3,UnitsPerBox=30,BoxesPerCarton=10,Boxes=20,TotalUnits=600,MrpAmount=i==1?600:300,MrpUnit="Box",MrpUnits=30,Status=ReceivingStatus.Approved,CreatedByUserId=admin.Id,CreatedAt=DateTimeOffset.UtcNow,ReviewedByUserId=admin.Id,ReviewedAt=DateTimeOffset.UtcNow,AutomaticApproval=true,MrpVerifiedByUserId=admin.Id,MrpVerifiedAt=DateTimeOffset.UtcNow};
            var batch = new MedicineBatch {Medicine=medicine,BatchNumber=revision.BatchNumber,ManufacturingDate=revision.ManufacturingDate,ExpiryDate=expiry};
            db.AddRange(receipt,revision,batch,new ReceivingMovement{ReceiptId=receipt.Id,Revision=revision,Batch=batch,Quantity=600,PostedByUserId=admin.Id,PostedAt=DateTimeOffset.UtcNow},new MedicineBarcode{Medicine=medicine,Code=$"DEMO-BOX-{i+1}",Unit="Box",Units=30,CreatedBy=admin.Id});
        }
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE pharmacy_demo_marker (version integer NOT NULL); INSERT INTO pharmacy_demo_marker VALUES (1)");
        await tx.CommitAsync();
    }
    static void Ensure(IdentityResult result) { if(!result.Succeeded) throw new InvalidOperationException("Demo account setup failed."); }
    public static void MapDemo(this WebApplication app) {
        if (!Enabled(app.Environment)) return;
        app.MapPost("/api/demo/login/{role}", async (string role, HttpContext http, IAntiforgery csrf, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) => {
            try { await csrf.ValidateRequestAsync(http); } catch(AntiforgeryValidationException) { return Results.BadRequest(); }
            if(role is not ("admin" or "operator")) return Results.BadRequest();
            var user = await users.FindByIdAsync("demo-"+role);
            if(user==null || !await users.IsInRoleAsync(user,role=="admin"?"Admin":"Operator")) return Results.Conflict();
            await signIn.SignInAsync(user,false); return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("login");
    }
}
