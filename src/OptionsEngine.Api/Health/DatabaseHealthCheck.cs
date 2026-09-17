using Microsoft.Extensions.Diagnostics.HealthChecks;
using OptionsEngine.Infrastructure.Persistence;

namespace OptionsEngine.Api.Health;

internal sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OptionsEngineDbContext>();

        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQLite database is reachable.")
                : HealthCheckResult.Unhealthy("SQLite database is not reachable.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Database health check failed");
            return HealthCheckResult.Unhealthy("SQLite database health check failed.", exception);
        }
    }
}
