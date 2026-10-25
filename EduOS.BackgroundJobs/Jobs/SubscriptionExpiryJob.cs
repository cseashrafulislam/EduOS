using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs;

/// <summary>
/// Host-only expiry reconciliation. Payment approval/renewal belongs to the existing
/// subscription payment owner; an AutoRenew preference never authorizes a charge.
/// </summary>
public sealed class SubscriptionExpiryJob(
    IServiceScopeFactory scopes, ILogger<SubscriptionExpiryJob> logger, TimeProvider clock)
{
    public static bool ShouldExpire(TenantSubscription row, DateTime utcNow) =>
        row.TenantId > 0 && !row.IsDeleted && row.EndsAt <= utcNow &&
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
            // Host-only listing. Never track cross-tenant rows on the listing context.
            var batch = await listingDb.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id > cursor && x.TenantId > 0 && !x.IsDeleted &&
                    x.EndsAt <= now && (x.State == SubscriptionState.Trial ||
                    x.State == SubscriptionState.Active || x.State == SubscriptionState.Grace))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.TenantId })
                .Take(100).ToListAsync(ct);
            if (batch.Count == 0) break;
            cursor = batch[^1].Id;
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // A new context and explicit system scope for each tenant-owned write.
                    using var tenantScope = scopes.CreateScope();
                    var db = tenantScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
                    using var systemScope = db.BeginSystemTenantScope(item.TenantId);
                    var row = await db.TenantSubscriptions.FirstOrDefaultAsync(x =>
                        x.Id == item.Id && x.TenantId == item.TenantId && !x.IsDeleted, ct);
                    if (row == null || !ShouldExpire(row, now)) continue;
                    row.State = SubscriptionState.Expired;
                    row.UpdatedAt = now;
                    row.UpdatedBy = null;
                    // DbContext performs tenant checks, optimistic RowVersion checks and audit.
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "Subscription expiry failed for tenant {TenantId}, subscription {SubscriptionId}",
                        item.TenantId, item.Id);
                }
            }
        }
        if (failures > 0)
            throw new InvalidOperationException(
                $"Subscription expiry reconciliation failed for {failures} subscription(s); Hangfire will retry.");
    }
}
