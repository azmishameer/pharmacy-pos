using PharmacyPos.Api.Returns;
using PharmacyPos.Api.Health;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Auth;

var createAdmin = args.Contains("--create-admin");
var resetAdmin = args.Contains("--reset-admin-password");
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--create-admin" && x != "--reset-admin-password").ToArray());

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<PharmacyDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("Pharmacy");
    var password = builder.Configuration["Database:Password"];
    if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrEmpty(password))
        throw new InvalidOperationException("Database settings are missing. Configure development User Secrets.");

    var settings = new NpgsqlConnectionStringBuilder(connection) { Password = password };
    options.UseNpgsql(settings.ConnectionString);
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.AddStaffAuthentication();

var app = builder.Build();
if (createAdmin || resetAdmin)
{
    try {
        if (createAdmin && resetAdmin) throw new InvalidOperationException("Choose only one account command.");
        if (resetAdmin) await FirstAdminSetup.ResetAsync(app.Services);
        else await FirstAdminSetup.RunAsync(app.Services);
    }
    catch (Exception error) when (error is InvalidOperationException or NpgsqlException)
    {
        Console.Error.WriteLine(error is NpgsqlException
            ? "Database setup failed. Check the connection and apply migrations first."
            : error.Message);
        Environment.ExitCode = 1;
    }
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Development diagnostic only; production health access will be configured at deployment.
    app.MapHealthChecks("/api/status/database");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaffAuthentication();
app.MapStaffManagement();
app.MapSalesReports();
app.MapPurchaseCosts();
app.MapCataloguePreview();
app.MapStockReceiving();
app.MapSalesCounter();
app.MapChargeRules();
app.MapOfferRules();
app.MapCashCheckout();
app.MapReturnEndpoints();

// API availability is separate from the database connection check.
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program { }
public sealed class PharmacyApiMarker { }
