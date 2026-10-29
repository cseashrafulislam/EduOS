using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionActiveCapacityPolicyTests
{
    [Fact]
    public async Task Student_limit_counts_active_only_and_reserves_the_whole_new_batch()
    {
        await using var db = Context(42);
        await Plan(db, 42, 2, 1);
        db.Students.AddRange(
            new Student { TenantId = 42, StudentCode = "ACTIVE", FullName = "Active learner", StatusCode = "Active" },
            new Student { TenantId = 42, StudentCode = "SUSP", FullName = "Suspended learner", StatusCode = "Suspended" });
        await db.SaveChangesAsync();

        Assert.True(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 42, 1));
        Assert.False(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 42, 2));
        Assert.False(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 42, 0));
    }

    [Fact]
    public async Task Employee_limit_applies_to_active_not_suspended_staff()
    {
        await using var db = Context(42);
        await Plan(db, 42, 0, 2);
        db.Employees.AddRange(
            new Employee { TenantId = 42, EmployeeCode = "A", FullName = "Active", State = EmployeeState.Active },
            new Employee { TenantId = 42, EmployeeCode = "S", FullName = "Suspended", State = EmployeeState.Suspended });
        await db.SaveChangesAsync();

        Assert.True(await SubscriptionActiveCapacityPolicy.CanAddEmployeesAsync(db, 42, 1));
        Assert.False(await SubscriptionActiveCapacityPolicy.CanAddEmployeesAsync(db, 42, 2));
        Assert.True(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 42, 1000));
    }

    [Fact]
    public async Task Missing_plan_is_unlimited_but_wrong_tenant_fails_closed()
    {
        await using var db = Context(42);
        Assert.True(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 42, 1));
        Assert.False(await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, 99, 1));
        Assert.False(await SubscriptionActiveCapacityPolicy.CanAddEmployeesAsync(db, 99, 1));
    }

    private static EduOSDbContext Context(long tenantId)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("active-quota-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private static async Task Plan(EduOSDbContext db, long tenant, int students, int employees)
    {
        var plan = new SubscriptionPlan
        {
            Code = "CAP-" + Guid.NewGuid().ToString("N")[..8], Name = "Capacity test",
            MaxStudents = students, MaxEmployees = employees
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant, SubscriptionPlanId = plan.Id, State = SubscriptionState.Active,
            StartsAt = DateTime.UtcNow.AddHours(-1), EndsAt = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
    }
}
