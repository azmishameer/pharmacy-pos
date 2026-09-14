using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace PharmacyPos.Api.Health;

public sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Pharmacy");
        var password = configuration["Database:Password"];

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrEmpty(password))
            return HealthCheckResult.Unhealthy("Database configuration is missing.");

        try
        {
            // Assign the password as a property so punctuation cannot alter connection settings.
            var settings = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Password = password,
                Timeout = 5,
                CommandTimeout = 5
            };

            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // A read-only query verifies that the configured account can use the database.
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is int value && value == 1
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database check failed.");
        }
        catch (Exception exception) when (exception is NpgsqlException
            or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            // Do not expose connection details or credentials in diagnostics or health logs.
            return HealthCheckResult.Unhealthy("Database connection could not be verified.");
        }
    }
}
