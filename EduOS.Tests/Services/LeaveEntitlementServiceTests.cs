using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LeaveEntitlementServiceTests
{
    [Fact]
    public async Task Balance_reserves_pending_leave_and_excludes_other_tenants_and_employees()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 10L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("leave-entitlement-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var own = new Employee { TenantId = 10, PublicId = Guid.NewGuid(), EmployeeCode = "OWN", FullName = "Own",
            PersonId = 1, DesignationId = 1, JoiningDate = new DateOnly(2025, 1, 1) };
        var other = new Employee { TenantId = 10, PublicId = Guid.NewGuid(), EmployeeCode = "OTHER", FullName = "Other",
            PersonId = 2, DesignationId = 1, JoiningDate = new DateOnly(2025, 1, 1) };
        var foreign = new Employee { TenantId = 20, PublicId = Guid.NewGuid(), EmployeeCode = "FOREIGN", FullName = "Foreign",
            PersonId = 3, DesignationId = 1, JoiningDate = new DateOnly(2025, 1, 1) };
        var type = new LeaveType { TenantId = 10, Name = "Casual", Code = "CAS", MaxDaysPerYear = 10, IsActive = true };
        db.Employees.AddRange(own, other, foreign); db.LeaveTypes.Add(type);
        await TestTenantSeeder.SaveAsync(db);
        db.EmployeeLeaveEntitlements.Add(new EmployeeLeaveEntitlement { TenantId = 10, EmployeeId = own.Id,
            LeaveTypeId = type.Id, Year = 2026, EntitledDays = 12, CarriedForwardDays = 1 });
        db.EmployeeLeaveAdjustments.Add(new EmployeeLeaveAdjustment { TenantId = 10, EmployeeId = own.Id,
            LeaveTypeId = type.Id, Year = 2026, Days = -1, Reason = "Policy correction", ApprovedByUserId = 99 });
        db.EmployeeLeaveApplications.AddRange(
            Leave(10, own.Id, type.Id, 2026, 3, LeaveState.Approved),
            Leave(10, own.Id, type.Id, 2026, 2, LeaveState.Submitted),
            Leave(10, other.Id, type.Id, 2026, 5, LeaveState.Approved),
            Leave(20, foreign.Id, type.Id, 2026, 6, LeaveState.Approved));
        await TestTenantSeeder.SaveAsync(db);
        var service = new LeaveEntitlementService(db, new TestUser(10, true), TimeProvider.System);
        var response = await service.GetBalancesAsync(own.PublicId, 2026);
        response.Success.Should().BeTrue();
        var balance = response.Data!.Single();
        balance.AllowedDays.Should().Be(12m);
        balance.UsedDays.Should().Be(3m);
        balance.RemainingDays.Should().Be(7m);
    }

    [Fact]
    public async Task Non_admin_cannot_read_tenant_wide_entitlements()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 10L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("leave-permission-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var result = await new LeaveEntitlementService(db, new TestUser(10, false), TimeProvider.System)
            .GetLeaveTypesAsync();
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    private static EmployeeLeaveApplication Leave(long tenant, long employeeId, long typeId, int year, int days, LeaveState state) => new()
    {
        TenantId = tenant, EmployeeId = employeeId, LeaveTypeId = typeId,
        FromDate = new DateOnly(year, 4, 1), ToDate = new DateOnly(year, 4, days),
        TotalDays = days, State = state, Reason = "Test leave", ClientRequestId = Guid.NewGuid()
    };
    private sealed class TestUser(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => "test@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
