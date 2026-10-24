using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionPlanAvailabilityTests
{
    [Fact]
    public async Task Superadmin_can_hide_plan_and_retry_without_duplicate_audit()
    {
        await using var db = Context();
        var plan = new SubscriptionPlan
        {
            Code = "TEST_PRO", Name = "Test Pro", IsActive = true, IsPublic = true,
            MonthlyPrice = 100, YearlyPrice = 1000, RowVersion = [1, 2, 3]
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = new ChangeSubscriptionPlanAvailabilityRequestDto
        {
            RequestId = Guid.NewGuid(), IsActive = true, IsPublic = false,
            RowVersion = Convert.ToBase64String(plan.RowVersion)
        };
        var changed = await service.ChangePlanAvailabilityAsync(plan.Id, request);
        changed.Success.Should().BeTrue(changed.Message);
        changed.Data!.IsPublic.Should().BeFalse();
        (await service.ChangePlanAvailabilityAsync(plan.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "SubscriptionPlan.AvailabilityChanged")).Should().Be(1);
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_change_host_plans()
    {
        await using var db = Context();
        var service = Service(db, new User(false));
        (await service.GetAdminPlanAvailabilityAsync()).StatusCode.Should().Be(403);
        (await service.ChangePlanAvailabilityAsync(1, new ChangeSubscriptionPlanAvailabilityRequestDto
        {
            RequestId = Guid.NewGuid(), IsActive = false, IsPublic = false, RowVersion = "AQID"
        })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Stale_row_version_rejected_without_audit()
    {
        await using var db = Context();
        var plan = new SubscriptionPlan
        {
            Code = "TEST_PRO", Name = "Test Pro", IsActive = true, IsPublic = true,
            RowVersion = [1, 2, 3]
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        var result = await Service(db, new User(true)).ChangePlanAvailabilityAsync(plan.Id,
            new ChangeSubscriptionPlanAvailabilityRequestDto
            {
                RequestId = Guid.NewGuid(), IsActive = false, IsPublic = false, RowVersion = "CQkJ"
            });
        result.StatusCode.Should().Be(409);
        plan.IsActive.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "SubscriptionPlan.AvailabilityChanged")).Should().Be(0);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("plan-availability-" + Guid.NewGuid().ToString("N")).Options);

    private static SubscriptionPlanService Service(EduOSDbContext db, ICurrentUserService user) => new(
        null!, null!, null!, null!, NullLogger<SubscriptionPlanService>.Instance, db, user);

    private sealed class User(bool superAdmin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => superAdmin ? 0 : 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => superAdmin;
        public bool IsTenantAdmin => !superAdmin;
        public IReadOnlyList<string> Roles => superAdmin ? ["SuperAdmin"] : ["TenantAdmin"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
