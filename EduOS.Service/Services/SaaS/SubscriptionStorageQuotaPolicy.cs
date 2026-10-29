using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.SaaS;

/// <summary>
/// Counts committed, non-deleted file assets as institution storage usage.
/// A null plan limit means no applicable plan; zero means an unlimited plan.
/// Callers must reserve capacity inside a Serializable SQL transaction before
/// committing a new FileAsset so concurrent uploads cannot overfill a plan.
/// </summary>
public static class SubscriptionStorageQuotaPolicy
{
    public const long BytesPerMegabyte = 1024L * 1024L;

    public sealed record Usage(long UsedBytes, int? MaxStorageMb)
    {
        public long? LimitBytes => MaxStorageMb is > 0
            ? (long)MaxStorageMb.Value * BytesPerMegabyte : null;

        public bool CanAdd(long additionalBytes) => additionalBytes >= 0 &&
            (LimitBytes is null || (UsedBytes <= LimitBytes.Value &&
                additionalBytes <= LimitBytes.Value - UsedBytes));
    }

    public static async Task<Usage> GetAsync(EduOSDbContext db, long tenantId,
        DateTime utcNow, CancellationToken ct = default)
    {
        if (tenantId <= 0 || db.CurrentTenantId != tenantId)
            throw new InvalidOperationException("Tenant storage quota scope mismatch.");

        var currentPlanId = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted &&
                x.StartsAt <= utcNow && x.EndsAt > utcNow &&
                (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                 x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync(ct);

        var max = currentPlanId.HasValue
            ? await db.SubscriptionPlans.AsNoTracking()
                .Where(x => x.Id == currentPlanId.Value && !x.IsDeleted)
                .Select(x => (int?)x.MaxStorageMb).FirstOrDefaultAsync(ct)
            : null;
        var used = await db.FileAssets.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .SumAsync(x => (long?)x.SizeBytes, ct) ?? 0;
        return new Usage(Math.Max(0, used), max);
    }
}
