using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;
public sealed class FinanceDiscountAwardRegisterTests
{
    [Fact]
    public async Task Register_is_paged_tenant_scoped_and_retains_deactivated_awards()
    {
        await using var db = Database();
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Name = "Campus A" });
        db.Students.Add(new Student { Id = 1, TenantId = 41, StudentCode = "ST-41", FullName = "Student" });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 1, TenantId = 41, StudentId = 1, CampusId = 1, RollNo = "R-1" });
        db.DiscountRules.Add(new DiscountRule { Id = 1, TenantId = 41, Name = "Merit", IsPercentage = true, Value = 10m });
        db.StudentDiscounts.AddRange(
            new StudentDiscount { Id = 1, TenantId = 41, StudentEnrollmentId = 1, DiscountRuleId = 1,
                EffectiveFrom = new DateOnly(2026, 10, 1), IsActive = true, ApprovedByUserId = 9 },
            new StudentDiscount { Id = 2, TenantId = 41, StudentEnrollmentId = 1, DiscountRuleId = 1,
                EffectiveFrom = new DateOnly(2026, 9, 1), IsActive = false, ApprovedByUserId = 9 });
        await TestTenantSeeder.SaveAsync(db);
        var service = new FinanceAdjustmentService(db, new User(41, true), TimeProvider.System);
        var first = await service.GetDiscountAwardRegisterAsync(null, null, null, null, 1, 1);
        first.StatusCode.Should().Be(200);
        first.Data!.TotalCount.Should().Be(2);
        first.Data.ActiveCount.Should().Be(1);
        first.Data.Awards.Items.Should().ContainSingle().Which.StudentCode.Should().Be("ST-41");
        var inactive = await service.GetDiscountAwardRegisterAsync(null, null, false, null);
        inactive.Data!.Awards.Items.Should().ContainSingle().Which.IsActive.Should().BeFalse();
        (await service.GetDiscountAwardRegisterAsync(999, null, null, null)).StatusCode.Should().Be(404);
    }
    [Fact]
    public async Task Unauthorized_and_invalid_queries_fail_closed()
    {
        await using var db = Database();
        (await new FinanceAdjustmentService(db, new User(41, false), TimeProvider.System)
            .GetDiscountAwardRegisterAsync(null, null, null, null)).StatusCode.Should().Be(403);
        (await new FinanceAdjustmentService(db, new User(42, true), TimeProvider.System)
            .GetDiscountAwardRegisterAsync(null, null, null, null)).StatusCode.Should().Be(403);
        (await new FinanceAdjustmentService(db, new User(41, true), TimeProvider.System)
            .GetDiscountAwardRegisterAsync(null, null, null, null, 1, 101)).StatusCode.Should().Be(400);
    }
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("discount-awards-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }
    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
