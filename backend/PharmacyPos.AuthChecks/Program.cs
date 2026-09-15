using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using PharmacyPos.Api.Data;

var connection = Environment.GetEnvironmentVariable("PHARMACY_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set PHARMACY_TEST_CONNECTION to an isolated disposable test database.");
if (!connection.Contains("pharmacy_auth_test", StringComparison.Ordinal))
    throw new InvalidOperationException("Test connection must name a pharmacy_auth_test database.");
using var factory = new AuthFactory(connection);
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
    await db.Database.MigrateAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Manager", "Operator" })
        Assert((await roles.CreateAsync(new IdentityRole(role))).Succeeded, "role creation");
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    foreach (var name in new[] { "admin-test", "operator-test", "locked-test" })
    {
        var user = new IdentityUser(name) { LockoutEnabled = true };
        Assert((await users.CreateAsync(user, "Temporary-Test!123")).Succeeded, "test account creation");
        Assert((await users.AddToRoleAsync(user, name == "admin-test" ? "Admin" : "Operator")).Succeeded, "role assignment");
        Assert(user.PasswordHash != "Temporary-Test!123", "password stored as a hash");
    }
}
Assert((await client.GetAsync("/api/medicines")).StatusCode == HttpStatusCode.Unauthorized, "anonymous catalogue blocked");
var session = await Session(client);
Assert(session.GetProperty("user").ValueKind == JsonValueKind.Null, "anonymous session");
Assert((await client.PostAsJsonAsync("/api/auth/login", new { username = "operator-test", password = "Temporary-Test!123" })).StatusCode == HttpStatusCode.BadRequest, "login requires CSRF token");
Assert((await Login(client, "operator-test", "wrong-password")).StatusCode == HttpStatusCode.Unauthorized, "wrong password rejected");
Assert((await Login(client, "operator-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.NoContent, "operator sign-in");
Assert((await client.GetAsync("/api/medicines")).StatusCode == HttpStatusCode.OK, "operator catalogue access");
session = await Session(client);
Assert(session.GetProperty("user").GetProperty("roles")[0].GetString() == "Operator", "operator role returned");
using (var scope = factory.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
    var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
    var op = await principalFactory.CreateAsync((await users.FindByNameAsync("operator-test"))!);
    var admin = await principalFactory.CreateAsync((await users.FindByNameAsync("admin-test"))!);
    Assert(!(await authorization.AuthorizeAsync(op, null, "AdminOnly")).Succeeded, "operator cannot satisfy admin policy");
    Assert((await authorization.AuthorizeAsync(admin, null, "AdminOnly")).Succeeded, "admin satisfies admin policy");
}
Assert((await client.PostAsync("/api/auth/logout", null)).StatusCode == HttpStatusCode.BadRequest, "logout requires CSRF token");
using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
{
    logout.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    Assert((await client.SendAsync(logout)).StatusCode == HttpStatusCode.NoContent, "sign-out succeeds");
}
Assert((await client.GetAsync("/api/medicines")).StatusCode == HttpStatusCode.Unauthorized, "catalogue blocked after sign-out");
for (var attempt = 0; attempt < 5; attempt++)
    Assert((await Login(client, "locked-test", "wrong-password")).StatusCode == HttpStatusCode.Unauthorized, "failed login counted");
Assert((await Login(client, "locked-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.Unauthorized, "locked account rejects correct password");
Assert((await Login(client, "operator-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.NoContent, "tenth attempt allowed");
Assert((await Login(client, "operator-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.TooManyRequests, "eleventh attempt rate limited");
Console.WriteLine("PASS: authentication, CSRF, role permissions, sign-out, lockout, and rate limiting.");

// Separate host resets the login limiter while reusing only this disposable database.
using var entryFactory = new AuthFactory(connection);
using var adminClient = entryFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
using var operatorClient = entryFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
Assert((await Login(adminClient, "admin-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.NoContent, "admin entry login");
Assert((await Login(operatorClient, "operator-test", "Temporary-Test!123")).StatusCode == HttpStatusCode.NoContent, "operator entry login");
var medicine = new
{
    requestId = Guid.NewGuid(), brandName = "Jardimet test", manufacturer = "Beximco Pharmaceuticals Ltd.",
    classification = "Prescription",
    ingredients = new[] {
        new { name = "Empagliflozin", strengthValue = 5m, strengthUnit = "mg" },
        new { name = "Metformin Hydrochloride", strengthValue = 500m, strengthUnit = "mg" }
    }
};

Assert((await adminClient.PostAsJsonAsync("/api/medicines", medicine)).StatusCode == HttpStatusCode.BadRequest, "medicine save requires CSRF");
Assert((await PostMedicine(adminClient, medicine with { classification = "Unknown" })).StatusCode == HttpStatusCode.BadRequest, "invalid classification rejected");
Assert((await PostMedicine(adminClient, medicine with { ingredients = new[] { new { name = "Empagliflozin", strengthValue = 0m, strengthUnit = "mg" } } })).StatusCode == HttpStatusCode.BadRequest, "zero strength rejected");
Assert((await PostMedicine(adminClient, medicine)).StatusCode == HttpStatusCode.Created, "medicine creation");
Assert((await PostMedicine(adminClient, medicine)).StatusCode == HttpStatusCode.OK, "safe request replay");
Assert((await PostMedicine(adminClient, medicine with { brandName = "Changed payload" })).StatusCode == HttpStatusCode.Conflict, "changed replay rejected");
Assert((await PostMedicine(adminClient, medicine with { requestId = Guid.NewGuid() })).StatusCode == HttpStatusCode.Conflict, "duplicate product rejected");
var search = await adminClient.GetFromJsonAsync<JsonElement>("/api/medicines?search=metformin");
Assert(search.GetProperty("items").GetArrayLength() == 1, "ingredient search finds saved medicine");
Assert(search.GetProperty("items")[0].GetProperty("ingredients")[1].GetProperty("strengthValue").GetDecimal() == 500m, "combination strength preserved");
var race = medicine with { requestId = Guid.NewGuid(), brandName = "Concurrent test" };
var saves = await Task.WhenAll(PostMedicine(adminClient, race), PostMedicine(adminClient, race with { requestId = Guid.NewGuid() }));
Assert(saves.Count(x => x.StatusCode == HttpStatusCode.Created) == 1 && saves.Count(x => x.StatusCode == HttpStatusCode.Conflict) == 1, "concurrent duplicate prevented");
using (var scope = entryFactory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
    var saved = await db.Medicines.SingleAsync(x => x.Id == medicine.requestId);
    Assert(saved.CreatedByUserId is not null && saved.CreatedAt is not null && saved.CreationRequestHash?.Length == 64, "creation attribution recorded");
    Assert(await db.Manufacturers.CountAsync() == 1 && await db.GenericIngredients.CountAsync() == 2, "reference names reused");
}
Console.WriteLine("PASS: admin medicine creation, validation, CSRF, safe replay, duplicate protection, search, and attribution.");

// Operator entry and batch metadata must remain separate from the approved catalogue.
var pendingMedicine = new {
    requestId = Guid.NewGuid(), brandName = "Operator syrup test", manufacturer = "Other manufacturer test",
    classification = "Prescription", dosageForm = "Syrup", baseUnit = "Bottle",
    firstBatch = new { batchNumber = "B-001", manufacturingDate = "2026-01-01", expiryDate = "2028-01-01" },
    ingredients = new[] { new { name = "Liquid ingredient", strengthValue = 120m, strengthUnit = "mg/5mL" } }
};
Assert((await PostMedicine(operatorClient, pendingMedicine)).StatusCode == HttpStatusCode.Created, "operator can submit medicine");
Assert((await PostMedicine(operatorClient, pendingMedicine)).StatusCode == HttpStatusCode.OK, "operator retry idempotent");
var hidden = await operatorClient.GetFromJsonAsync<JsonElement>("/api/medicines?search=Operator%20syrup");
Assert(hidden.GetProperty("items").GetArrayLength() == 0, "pending medicine hidden from catalogue");
var submissions = await adminClient.GetFromJsonAsync<JsonElement>("/api/catalogue/submissions");
Assert(submissions.GetArrayLength() == 1 && submissions[0].GetProperty("batches")[0].GetProperty("expiryDate").GetString() == "2028-01-01", "admin reviews batch dates");
var reviewUrl = $"/api/catalogue/submissions/{pendingMedicine.requestId}/review";
Assert((await PostReview(operatorClient, reviewUrl, true)).StatusCode == HttpStatusCode.Forbidden, "operator cannot approve own entry");
Assert((await adminClient.PostAsJsonAsync(reviewUrl, new { approve = true })).StatusCode == HttpStatusCode.BadRequest, "review requires CSRF");
Assert((await PostReview(adminClient, reviewUrl, false)).StatusCode == HttpStatusCode.BadRequest, "rejection requires reason");
Assert((await PostReview(adminClient, reviewUrl, true)).StatusCode == HttpStatusCode.NoContent, "admin approves entry");
Assert((await PostReview(adminClient, reviewUrl, false, "Late review")).StatusCode == HttpStatusCode.Conflict, "review cannot overwrite approval");
var visible = await operatorClient.GetFromJsonAsync<JsonElement>("/api/medicines?search=Operator%20syrup");
Assert(visible.GetProperty("items").GetArrayLength() == 1, "approved medicine visible");
Assert((await PostMedicine(adminClient, pendingMedicine with { requestId = Guid.NewGuid(), brandName = "Bad dates", firstBatch = pendingMedicine.firstBatch with { manufacturingDate = "2029-01-01" } })).StatusCode == HttpStatusCode.BadRequest, "manufacture after expiry blocked");
Assert((await PostMedicine(adminClient, pendingMedicine with { requestId = Guid.NewGuid(), dosageForm = "Fake" })).StatusCode == HttpStatusCode.BadRequest, "unsupported dosage form blocked");
var rejected = pendingMedicine with { requestId = Guid.NewGuid(), brandName = "Rejected syrup" };
Assert((await PostMedicine(operatorClient, rejected)).StatusCode == HttpStatusCode.Created, "second operator entry");
Assert((await PostReview(adminClient, $"/api/catalogue/submissions/{rejected.requestId}/review", false, "Wrong label details")).StatusCode == HttpStatusCode.NoContent, "admin rejects with reason");
Assert((await PostMedicine(adminClient, rejected with { requestId = Guid.NewGuid() })).StatusCode == HttpStatusCode.Created, "admin can reenter rejected medicine");
using (var scope = entryFactory.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
    var record = await db.Medicines.Include(x => x.Batches).SingleAsync(x => x.Id == pendingMedicine.requestId);
    Assert(record.ReviewedByUserId != record.CreatedByUserId && record.ReviewedAt is not null, "review attribution stored");
    Assert(record.Batches.Count == 1 && record.BaseUnit.ToString() == "Bottle", "retry creates one batch and preserves stock unit");
}
Console.WriteLine("PASS: operator submission, hidden pending catalogue, batch dates, review authorization, rejection, reentry, and review attribution.");

await StockChecks.Run(adminClient, operatorClient, entryFactory.Services, medicine.requestId);
using var anonymousSalesClient = entryFactory.CreateClient();
await SalesChecks.Run(adminClient, operatorClient, anonymousSalesClient, entryFactory.Services, medicine.requestId);
await ChargeChecks.Run(adminClient, operatorClient, anonymousSalesClient, entryFactory.Services, medicine.requestId, pendingMedicine.requestId);
await OfferChecks.Run(adminClient, operatorClient, anonymousSalesClient, entryFactory.Services, medicine.requestId);
await CheckoutChecks.Run(adminClient, operatorClient, anonymousSalesClient, entryFactory.Services, medicine.requestId);
await ReturnChecks.Run(adminClient, operatorClient, anonymousSalesClient, entryFactory.Services, medicine.requestId);

static async Task<HttpResponseMessage> PostReview(HttpClient client, string url, bool approve, string? note = null) {
    var session = await Session(client);
    var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { approve, note }) };
    request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    return await client.SendAsync(request);
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
}
static async Task<JsonElement> Session(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/auth/session"));
static async Task<HttpResponseMessage> PostMedicine(HttpClient client, object payload)
{
    var session = await Session(client);
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/medicines") { Content = JsonContent.Create(payload) };
    request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    return await client.SendAsync(request);
}
static async Task<HttpResponseMessage> Login(HttpClient client, string name, string password)
{
    var session = await Session(client);
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { username = name, password })
    };
    request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    return await client.SendAsync(request);
}

sealed class AuthFactory(string connection) : WebApplicationFactory<PharmacyApiMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../PharmacyPos.Api")));
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pharmacy"] = connection,
                ["Database:Password"] = "disposable-test-only",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
    }
}
