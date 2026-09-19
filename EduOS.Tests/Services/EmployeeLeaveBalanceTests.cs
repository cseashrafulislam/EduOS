using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Portals;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class EmployeeLeaveBalanceTests
{
    [Fact]
    public async Task Leave_balances_are_scoped_to_current_employee_tenant_and_year()
    {
        await using var context = CreateContext(out var accessor);
        context.Set<Employee>().Add(Employee(10, 99));
        var casual = new LeaveType { TenantId = 10, Name = "Casual", MaxDaysPerYear = 10, IsActive = true };
        var sick = new LeaveType { TenantId = 10, Name = "Sick", MaxDaysPerYear = 8, IsActive = true };
        var otherTenant = new LeaveType { TenantId = 20, Name = "Other", MaxDaysPerYear = 99, IsActive = true };
        context.LeaveTypes.AddRange(casual, sick, otherTenant);
        await context.SaveChangesAsync();
        var year = DateTime.UtcNow.Year;
        context.LeaveApplications.AddRange(
            Leave(10, 99, casual.Id, year, 3, "Approved"),
            Leave(10, 99, casual.Id, year, 2, "Pending"),
            Leave(10, 100, casual.Id, year, 4, "Approved"),
            Leave(10, 99, casual.Id, year - 1, 5, "Approved"),
            Leave(20, 99, otherTenant.Id, year, 7, "Approved"));
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetLeaveBalancesAsync(year);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        var casualBalance = result.Data!.Single(x => x.LeaveType == "Casual");
        casualBalance.AnnualEntitlement.Should().Be(10);
        casualBalance.UsedDays.Should().Be(3);
        casualBalance.PendingDays.Should().Be(2);
        casualBalance.RemainingDays.Should().Be(5);
        var sickBalance = result.Data.Single(x => x.LeaveType == "Sick");
        sickBalance.UsedDays.Should().Be(0);
        sickBalance.PendingDays.Should().Be(0);
        sickBalance.RemainingDays.Should().Be(8);
    }

    [Fact]
    public async Task Leave_balances_reject_invalid_year()
    {
        await using var context = CreateContext(out var accessor);
        SetTenant(accessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetLeaveBalancesAsync(1999);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private static EduOSDbContext CreateContext(out HttpContextAccessor accessor)
    {
        accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"employee-leave-balance-{Guid.NewGuid():N}").Options, accessor);
    }

    private static void SetTenant(HttpContextAccessor accessor, long tenantId) => accessor.HttpContext!.Items["TenantId"] = tenantId;

    private static EmployeeSelfServiceService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Employee>(context),
        new GenericRepository<EmployeeAttendance>(context),
        new GenericRepository<LeaveApplication>(context),
        new GenericRepository<LeaveType>(context),
        currentUser,
        NullLogger<EmployeeSelfServiceService>.Instance);

    private static Employee Employee(long tenantId, long userId) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        EmployeeCode = "EMP-99",
        FullName = "Employee",
        Phone = "01700000000",
        DesignationId = 1,
        JoiningDate = DateTime.UtcNow.Date,
        IsActive = true
    };

    private static LeaveApplication Leave(long tenantId, long userId, long leaveTypeId, int year, int days, string status) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        UserType = "Employee",
        LeaveTypeId = leaveTypeId,
        FromDate = new DateTime(year, 4, 1),
        ToDate = new DateTime(year, 4, days),
        TotalDays = days,
        Reason = "Test leave",
        Status = status
    };

    private sealed class TestCurrentUser(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Employee";
        public string? Email => "employee@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Staff"];
        public bool IsInRole(string role) => role == "Staff";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
