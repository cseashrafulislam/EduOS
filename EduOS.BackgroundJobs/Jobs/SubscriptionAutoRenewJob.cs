using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs;

/// <summary>Host-only pre-expiry invoice scheduling. Never charges or verifies payments.</summary>
public sealed class SubscriptionAutoRenewJob(
    IServiceScopeFactory scopes, ILogger<SubscriptionAutoRenewJob> logger, TimeProvider clock)
{
    public static bool ShouldSchedule(TenantSubscription row, DateTime now) =>
        row.TenantId > 0 && !row.IsDeleted && row.AutoRenew &&
        row.StartsAt <= now && row.EndsAt > now && row.EndsAt <= now.AddDays(30) &&
        row.BillingCycleCode != "Lifetime" &&
        row.State is SubscriptionState.Trial or SubscriptionState.Active or SubscriptionState.Grace;

    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        using var listingScope = scopes.CreateScope();
        var listingDb = listingScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        long cursor = 0;
        var failures = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await listingDb.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id > cursor && x.TenantId > 0 && !x.IsDeleted && x.AutoRenew &&
                    x.StartsAt <= now && x.EndsAt > now && x.EndsAt <= now.AddDays(30) &&
                    x.BillingCycleCode != "Lifetime" &&
                    (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                     x.State == SubscriptionState.Grace))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.TenantId })
                .Take(100).ToListAsync(ct);
            if (batch.Count == 0) break;
            cursor = batch[^1].Id;
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var tenantScope = scopes.CreateScope();
                    var db = tenantScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
                    using var systemScope = db.BeginSystemTenantScope(item.TenantId);
                    var row = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.Id == item.Id && x.TenantId == item.TenantId && !x.IsDeleted, ct);
                    if (row == null || !ShouldSchedule(row, now)) continue;
                    var service = tenantScope.ServiceProvider.GetRequiredService<SubscriptionService>();
                    var result = await service.ScheduleAutomaticRenewalAsync(item.TenantId, item.Id, ct);
                    if (!result.Success)
                    {
                        // Business conflicts (opt-out, changed plan, already pending renewal)
                        // are safe to skip. Operational failures are retried by Hangfire.
                        if (result.StatusCode >= 500)
                            throw new InvalidOperationException(
                                $"Auto-renew scheduling failed for tenant {item.TenantId}: {result.StatusCode}");
                        logger.LogWarning("Auto-renew skipped for tenant {TenantId}, subscription {SubscriptionId}: {Status}",
                            item.TenantId, item.Id, result.StatusCode);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "Automatic renewal failed for tenant {TenantId}, subscription {SubscriptionId}",
                        item.TenantId, item.Id);
                }
            }
        }
        if (failures > 0)
            throw new InvalidOperationException(
                $"Automatic renewal scheduling failed for {failures} subscription(s); Hangfire will retry.");
    }
}
