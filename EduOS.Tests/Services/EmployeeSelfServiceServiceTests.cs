using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Portals;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class EmployeeSelfServiceServiceTests
{
    [Fact]
    public async Task Attendance_returns_only_current_users_rows_in_current_tenant()
    {
        await using var context = CreateContext(out var httpContextAccessor);
        var own = Employee(10, 99, "OWN-1");
        var other = Employee(10, 100, "OTHER-1");
        var otherTenant = Employee(20, 99, "OTHER-TENANT");
        context.Set<Employee>().AddRange(own, other, otherTenant);
        await context.SaveChangesAsync();
        context.EmployeeAttendances.AddRange(
            Attendance(10, own.Id, DateTime.UtcNow.Date.AddDays(-1), "Present"),
            Attendance(10, other.Id, DateTime.UtcNow.Date.AddDays(-1), "Absent"),
            Attendance(20, otherTenant.Id, DateTime.UtcNow.Date.AddDays(-1), "Late"));
        await context.SaveChangesAsync();
        SetTenant(httpContextAccessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].Status.Should().Be("Present");
    }

    [Fact]
    public async Task Attendance_rejects_inactive_employee_link()
    {
        await using var context = CreateContext(out var httpContextAccessor);
        var employee = Employee(10, 99, "INACTIVE");
        employee.IsActive = false;
        context.Set<Employee>().Add(employee);
        await context.SaveChangesAsync();
        SetTenant(httpContextAccessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Attendance_rejects_ranges_longer_than_one_year()
    {
        await using var context = CreateContext(out var httpContextAccessor);
        SetTenant(httpContextAccessor, 10);
        var service = CreateService(context, new TestCurrentUser(10, 99));

        var result = await service.GetAttendanceAsync(DateTime.UtcNow.Date.AddDays(-367), DateTime.UtcNow.Date);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Leave_history_returns_only_current_employee_user_in_current_tenant()
    {
        await using var context = CreateContext(out var httpContextAccessor);
        context.Set<Employee>().Add(Employee(10, 99, "OWN-LEAVE"));
        var ownType = new LeaveType { TenantId = 10, Name = "Casual", MaxDaysPerYear = 10 };
        var otherType = new LeaveType { TenantId = 20, Name = "Other tenant", MaxDaysPerYear = 10 };
        context.LeaveTypes.AddRange(ownType, otherType);
        await context.SaveChangesAsync();
        context.LeaveApplications.AddRange(
            Leave(10, 99, ownType.Id, "Employee", "Own leave"),
            Leave(10, 100, ownType.Id, "Employee", "Other user"),
            Leave(10, 99, ownType.Id, "Student", "Student leave"),
            Leave(20, 99, otherType.Id, "Employee", "Other tenant"));
        await context.SaveChangesAsync();
        SetTenant(httpContextAccessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetLeaveHistoryAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].LeaveType.Should().Be("Casual");
        result.Data[0].Reason.Should().Be("Own leave");
    }

    private static EduOSDbContext CreateContext(out HttpContextAccessor httpContextAccessor)
    {
        httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"employee-portal-{Guid.NewGuid():N}").Options, httpContextAccessor);
    }

    private static void SetTenant(HttpContextAccessor accessor, long tenantId) => accessor.HttpContext!.Items["TenantId"] = tenantId;

    private static EmployeeSelfServiceService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Employee>(context),
        new GenericRepository<EmployeeAttendance>(context),
        new GenericRepository<LeaveApplication>(context),
        new GenericRepository<LeaveType>(context),
        currentUser,
        NullLogger<EmployeeSelfServiceService>.Instance);

    private static Employee Employee(long tenantId, long userId, string code) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        EmployeeCode = code,
        FullName = code,
        Phone = "01700000000",
        DesignationId = 1,
        JoiningDate = DateTime.UtcNow.Date,
        IsActive = true
    };

    private static EmployeeAttendance Attendance(long tenantId, long employeeId, DateTime date, string status) => new()
    {
        TenantId = tenantId,
        EmployeeId = employeeId,
        Date = date,
        Status = status
    };

    private static LeaveApplication Leave(long tenantId, long userId, long leaveTypeId, string userType, string reason) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        UserType = userType,
        LeaveTypeId = leaveTypeId,
        FromDate = DateTime.UtcNow.Date,
        ToDate = DateTime.UtcNow.Date,
        TotalDays = 1,
        Reason = reason,
        Status = "Pending"
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
