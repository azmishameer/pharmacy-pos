using Microsoft.AspNetCore.DataProtection;
using PharmacyPos.Api.Maintenance;
using PharmacyPos.Api.Returns;
using PharmacyPos.Api.Health;
using PharmacyPos.Api.Catalogue;
using PharmacyPos.Api.Stock;
using PharmacyPos.Api.Sales;
using PharmacyPos.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyPos.Api.Auth;

var migrateDatabase = args.Contains("--migrate-database");
var backupDatabase = args.Contains("--backup-database");
var createAdmin = args.Contains("--create-admin");
var resetAdmin = args.Contains("--reset-admin-password");
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--create-admin" && x != "--reset-admin-password" && x != "--backup-database" && x != "--migrate-database").ToArray());

// Installations keep credentials and persistent data outside the versioned application.
var deploymentConfig = Environment.GetEnvironmentVariable("PHARMACY_CONFIG");
if (!string.IsNullOrWhiteSpace(deploymentConfig))
    builder.Configuration.AddJsonFile(Path.GetFullPath(deploymentConfig), optional: false, reloadOnChange: false).AddEnvironmentVariables();
builder.Host.UseWindowsService(options => options.ServiceName = "PharmacyPos");
var keysDirectory = builder.Configuration["DataProtection:Directory"];
if (!string.IsNullOrWhiteSpace(keysDirectory)) {
    Directory.CreateDirectory(keysDirectory);
    var protection = builder.Services.AddDataProtection().SetApplicationName("PharmacyPos")
        .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<PharmacyDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("Pharmacy");
    var password = builder.Configuration["Database:Password"];
    if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrEmpty(password))
        throw new InvalidOperationException("Database settings are missing. Configure the database connection and password for this installation.");

    var settings = new NpgsqlConnectionStringBuilder(connection) { Password = password };
    options.UseNpgsql(settings.ConnectionString);
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.AddStaffAuthentication();
builder.Services.AddHostedService<BackupWorker>();

var app = builder.Build();
if (createAdmin || resetAdmin || backupDatabase || migrateDatabase)
{
    try {
        if (new[] {createAdmin, resetAdmin, backupDatabase, migrateDatabase}.Count(x=>x)>1) throw new InvalidOperationException("Choose only one maintenance command.");
        if (migrateDatabase) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database.MigrateAsync(); Console.WriteLine("Database migrations completed."); }
        else if (backupDatabase) await DatabaseBackup.RunAsync(app.Configuration,app.Environment.ContentRootPath);
        else if (resetAdmin) await FirstAdminSetup.ResetAsync(app.Services);
        else await FirstAdminSetup.RunAsync(app.Services);
    }
    catch (Exception error) when (error is InvalidOperationException or NpgsqlException or IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
    {
        Console.Error.WriteLine(error is NpgsqlException
            ? "Database setup failed. Check the connection and apply migrations first."
            : error is System.ComponentModel.Win32Exception ? "PostgreSQL tools were not found. Set Backup__PostgresBin to their bin directory."
            : error is OperationCanceledException ? "Backup timed out; no completed backup was kept."
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

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaffAuthentication();
app.MapAutomaticBackups();
app.MapStaffManagement();
app.MapSalesReports();
app.MapProfitReports();
app.MapPurchaseCosts();
app.MapCataloguePreview();
app.MapStockReceiving();
app.MapStockAlerts();
app.MapSalesCounter();
app.MapChargeRules();
app.MapOfferRules();
app.MapCashCheckout();
app.MapReceiptSettings();
app.MapReturnEndpoints();

// API availability is separate from the database connection check.
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));
if (!app.Environment.IsDevelopment()) app.MapHealthChecks("/api/status/database").RequireAuthorization("AdminOnly");

// Keep unknown API routes from returning the website HTML.
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
public sealed class PharmacyApiMarker { }
