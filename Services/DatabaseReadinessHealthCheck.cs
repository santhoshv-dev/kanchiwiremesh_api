using KanchimeshAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KanchimeshAPI.Services;

public sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KanchimeshDbContext>();

            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            if (!database.Database.IsInMemory() &&
                (await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                return HealthCheckResult.Unhealthy("Database migrations are pending.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database readiness check failed.", exception);
        }
    }
}
