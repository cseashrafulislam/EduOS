using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs;

/// <summary>
/// Trusted host-only scheduler: one fresh DbContext and explicit tenant scope per institution.
/// Does not impersonate an administrator, cross tenant contexts, or backfill historical counts.
/// </summary>
public sealed class DailyUsageSnapshotJob(
    IServiceScopeFactory scopes, ILogger<DailyUsageSnapshotJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var listingScope = scopes.CreateScope();
        var listingDb = listingScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        long cursor = 0;
        var failures = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Keyset paging bounds memory and avoids changing the tenant scope of the listing DbContext.
            var ids = await listingDb.Tenants.AsNoTracking()
                .Where(x => x.Id > cursor && !x.IsDeleted &&
                    x.State == TenantState.Active && x.OnboardingStage == OnboardingStage.Completed)
                .OrderBy(x => x.Id).Select(x => x.Id).Take(100).ToListAsync(ct);
            if (ids.Count == 0) break;
            cursor = ids[^1];
            foreach (var tenantId in ids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var tenantScope = scopes.CreateScope();
                    var db = tenantScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
                    using var systemScope = db.BeginSystemTenantScope(tenantId);
                    var result = await UsageHistoryService.CaptureForTenantAsync(
                        db, tenantId, null, DateTime.UtcNow, logger, ct);
                    if (!result.Success)
                        throw new InvalidOperationException(
                            $"Daily snapshot failed for tenant {tenantId}: {result.StatusCode}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "Scheduled usage snapshot failed for tenant {TenantId}", tenantId);
                }
            }
        }
        if (failures > 0)
            throw new InvalidOperationException(
                $"Scheduled usage snapshot failed for {failures} institution(s); Hangfire will retry.");
    }
}
