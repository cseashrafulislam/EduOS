using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduOS.App.Health;

/// <summary>
/// Readiness probe for the primary relational database. It deliberately performs
/// only a connectivity check and never mutates schema or tenant data.
/// </summary>
public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly EduOSDbContext _dbContext;
    private readonly ILogger<DatabaseReadinessHealthCheck> _logger;

    public DatabaseReadinessHealthCheck(EduOSDbContext dbContext, ILogger<DatabaseReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Primary database is reachable.")
                : HealthCheckResult.Unhealthy("Primary database is unreachable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Primary database readiness check failed.");
            return HealthCheckResult.Unhealthy("Primary database readiness check failed.");
        }
    }
}
