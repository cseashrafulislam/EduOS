using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionUserSeatPolicyTests
{
    [Fact]
    public async Task Counts_only_institution_reserved_seats_and_allows_departed_seats_to_be_reused()
    {
        await using var db = Context();
        var now = DateTime.UtcNow;
        await SeedPlanAsync(db, 101, 2, now);
        using (db.BeginSystemTenantScope(101))
        {
            db.TenantMemberships.AddRange(
                new TenantMembership { TenantId = 101, UserId = 11, IsOwner = true, Status = MembershipStatus.Active },
                new TenantMembership { TenantId = 101, UserId = 12, Status = MembershipStatus.Suspended });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        using (db.BeginSystemTenantScope(202))
        {
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = 202, UserId = 13, Status = MembershipStatus.Active
            });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        Assert.False(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 101, now));

        using (db.BeginSystemTenantScope(101))
        {
            var departed = await db.TenantMemberships.SingleAsync(x => x.UserId == 12);
            departed.Status = MembershipStatus.Left;
            await db.SaveChangesAsync();
        }
        Assert.True(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 101, now));
        Assert.False(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 0, now));
    }

    [Fact]
    public async Task Unlimited_or_expired_plans_do_not_infer_a_limit()
    {
        await using var db = Context();
        var now = DateTime.UtcNow;
        await SeedPlanAsync(db, 101, 0, now);
        using (db.BeginSystemTenantScope(101))
        {
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = 101, UserId = 20, Status = MembershipStatus.Active
            });
            await db.SaveChangesAsync();
        }
        Assert.True(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 101, now));
        Assert.True(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 101, now.AddDays(3)));
    }

    [Fact]
    public async Task Active_grace_plan_limit_applies_to_same_tenant()
    {
        await using var db = Context();
        var now = DateTime.UtcNow;
        await SeedPlanAsync(db, 101, 1, now, SubscriptionState.Grace);
        using (db.BeginSystemTenantScope(101))
        {
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = 101, UserId = 20, Status = MembershipStatus.Active
            });
            await db.SaveChangesAsync();
        }
        Assert.False(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 101, now));
        Assert.True(await SubscriptionUserSeatPolicy.CanAddMemberAsync(db, 202, now));
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("user-seat-" + Guid.NewGuid().ToString("N")).Options);

    private static async Task SeedPlanAsync(EduOSDbContext db, long tenantId, int seats,
        DateTime now, SubscriptionState state = SubscriptionState.Active)
    {
        var plan = new SubscriptionPlan
        {
            Code = "SEAT-" + Guid.NewGuid().ToString("N")[..8],
            Name = "User seat policy test", MaxUsers = seats
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        using (db.BeginSystemTenantScope(tenantId))
        {
            db.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenantId, SubscriptionPlanId = plan.Id, State = state,
                StartsAt = now.AddHours(-1), EndsAt = now.AddDays(1)
            });
            await db.SaveChangesAsync();
        }
    }
}
