using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.DTOs.SaaS;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionStorageQuotaPolicyTests
{
    [Fact]
    public async Task Tenant_storage_limit_uses_committed_file_bytes_and_excludes_foreign_tenants()
    {
        var databaseName = "storage-policy-" + Guid.NewGuid().ToString("N");
        await using (var foreignTenant = Context(99, databaseName))
        {
            foreignTenant.FileAssets.Add(Asset(99, 1048576));
            await foreignTenant.SaveChangesAsync();
        }

        await using var db = Context(42, databaseName);
        var now = DateTime.UtcNow;
        await SeedPlanAsync(db, 42, 1, now);
        db.FileAssets.Add(Asset(42, 1048575));
        await db.SaveChangesAsync();

        var usage = await SubscriptionStorageQuotaPolicy.GetAsync(db, 42, now);
        Assert.Equal(1048575L, usage.UsedBytes);
        Assert.Equal(1048576L, usage.LimitBytes);
        Assert.True(usage.CanAdd(1));
        Assert.False(usage.CanAdd(2));
        Assert.False(usage.CanAdd(-1));
    }

    [Fact]
    public async Task Unlimited_grace_plan_and_missing_plan_do_not_infer_a_storage_limit()
    {
        await using var db = Context(42);
        var now = DateTime.UtcNow;
        await SeedPlanAsync(db, 42, 0, now, SubscriptionState.Grace);
        db.FileAssets.Add(Asset(42, 2048));
        await db.SaveChangesAsync();

        var unlimited = await SubscriptionStorageQuotaPolicy.GetAsync(db, 42, now);
        Assert.Equal(0, unlimited.MaxStorageMb);
        Assert.Null(unlimited.LimitBytes);
        Assert.True(unlimited.CanAdd(long.MaxValue));

        var expired = await SubscriptionStorageQuotaPolicy.GetAsync(db, 42, now.AddDays(3));
        Assert.Null(expired.MaxStorageMb);
        Assert.True(expired.CanAdd(4096));
        Assert.False(UsageStorageQuotaMetricDto.FromBytes(2048, null).IsUnlimited);
        Assert.True(UsageStorageQuotaMetricDto.FromBytes(2048, 0).IsUnlimited);
    }

    [Fact]
    public async Task Wrong_tenant_scope_fails_closed()
    {
        await using var db = Context(42);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubscriptionStorageQuotaPolicy.GetAsync(db, 99, DateTime.UtcNow));
    }

    private static FileAsset Asset(long tenant, long bytes) => new()
    {
        TenantId = tenant, PublicId = Guid.NewGuid(),
        StorageProvider = "LocalPrivate", StorageKey = $"tenant-{tenant}/documents/{Guid.NewGuid():N}.pdf",
        OriginalFileName = "test.pdf", ContentType = "application/pdf", SizeBytes = bytes
    };

    private static EduOSDbContext Context(long tenantId, string? databaseName = null)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(databaseName ?? "storage-policy-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private static async Task SeedPlanAsync(EduOSDbContext db, long tenantId,
        int maxStorageMb, DateTime now, SubscriptionState state = SubscriptionState.Active)
    {
        var plan = new SubscriptionPlan
        {
            Code = "STOR-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Storage test", MaxStorageMb = maxStorageMb
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenantId, SubscriptionPlanId = plan.Id, State = state,
            StartsAt = now.AddHours(-1), EndsAt = now.AddDays(1)
        });
        await db.SaveChangesAsync();
    }
}
