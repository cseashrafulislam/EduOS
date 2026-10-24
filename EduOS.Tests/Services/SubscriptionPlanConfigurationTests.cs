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

public sealed class SubscriptionPlanConfigurationTests
{
    [Fact]
    public async Task Host_can_change_prices_limits_and_features_without_mutating_subscription_snapshot()
    {
        await using var db = Context();
        var plan = new SubscriptionPlan { Code = "TEST", Name = "Test", MonthlyPrice = 100,
            YearlyPrice = 1000, RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "LIBRARY", Name = "Library" };
        db.SubscriptionPlans.Add(plan);
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        var service = Service(db, true);
        var request = Request(plan, feature.Id);
        var result = await service.SavePlanConfigurationAsync(plan.Id, request);
        result.Success.Should().BeTrue(result.Message);
        plan.MonthlyPrice.Should().Be(250);
        plan.MaxStudents.Should().Be(500);
        (await db.PlanFeatures.SingleAsync()).IsEnabled.Should().BeTrue();
        (await service.SavePlanConfigurationAsync(plan.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "SubscriptionPlan.ConfigurationChanged")).Should().Be(1);
    }

    [Fact]
    public async Task Non_superadmin_cannot_read_or_modify_host_configuration()
    {
        await using var db = Context();
        var service = Service(db, false);
        (await service.GetPlanConfigurationAsync(1)).StatusCode.Should().Be(403);
        (await service.SavePlanConfigurationAsync(1, new())).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Rejects_stale_version_and_unknown_feature_without_mutation()
    {
        await using var db = Context();
        var plan = new SubscriptionPlan { Code = "TEST", Name = "Test", MonthlyPrice = 100,
            RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "LIBRARY", Name = "Library" };
        db.SubscriptionPlans.Add(plan);
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        var service = Service(db, true);
        var stale = Request(plan, feature.Id); stale.RowVersion = "CQkJ";
        (await service.SavePlanConfigurationAsync(plan.Id, stale)).StatusCode.Should().Be(409);
        var unknown = Request(plan, 999);
        (await service.SavePlanConfigurationAsync(plan.Id, unknown)).StatusCode.Should().Be(409);
        plan.MonthlyPrice.Should().Be(100);
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "SubscriptionPlan.ConfigurationChanged")).Should().Be(0);
    }

    private static SaveSubscriptionPlanConfigurationRequestDto Request(SubscriptionPlan plan, long featureId) => new()
    {
        RequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(plan.RowVersion),
        MonthlyPrice = 250, YearlyPrice = 2500, TrialDays = 14, MaxStudents = 500,
        MaxEmployees = 20, MaxUsers = 30, MaxCampuses = 3, MaxStorageMb = 1000,
        Features = [new() { FeatureId = featureId, IsEnabled = true, LimitValue = 100 }]
    };
    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("plan-configuration-" + Guid.NewGuid().ToString("N")).Options);
    private static SubscriptionPlanService Service(EduOSDbContext db, bool admin) => new(
        null!, null!, null!, null!, NullLogger<SubscriptionPlanService>.Instance, db, new User(admin));
    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => admin ? 0 : 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => admin;
        public bool IsTenantAdmin => !admin;
        public IReadOnlyList<string> Roles => admin ? ["SuperAdmin"] : ["TenantAdmin"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
