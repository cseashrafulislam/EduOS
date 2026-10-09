using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
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
public class EmployeeSelfServiceServiceTests
{
    [Fact]
    public async Task Attendance_returns_only_current_users_rows_in_current_tenant()
    {
        await using var context = CreateContext(out var accessor);
        var own = Employee(10, 99, "OWN"); var other = Employee(10, 100, "OTHER"); var foreign = Employee(20, 99, "FOREIGN");
        context.Set<Employee>().AddRange(own, other, foreign);
        await context.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        context.Set<EmployeeAttendance>().AddRange(
            Attendance(10, own.Id, date, AttendanceState.Present),
            Attendance(10, other.Id, date, AttendanceState.Absent),
            Attendance(20, foreign.Id, date, AttendanceState.Late));
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].Status.Should().Be("Present");
    }

    [Fact]
    public async Task Attendance_rejects_inactive_employee_link()
    {
        await using var context = CreateContext(out var accessor);
        var employee = Employee(10, 99, "INACTIVE"); employee.State = EmployeeState.Suspended;
        context.Set<Employee>().Add(employee);
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();
        result.Success.Should().BeFalse(); result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Attendance_rejects_ranges_longer_than_one_year()
    {
        await using var context = CreateContext(out var accessor); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99))
            .GetAttendanceAsync(DateTime.UtcNow.Date.AddDays(-367), DateTime.UtcNow.Date);
        result.Success.Should().BeFalse(); result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Leave_history_returns_only_current_employee_user_in_current_tenant()
    {
        await using var context = CreateContext(out var accessor);
        var own = Employee(10, 99, "OWN"); var other = Employee(10, 100, "OTHER"); var foreign = Employee(20, 99, "FOREIGN");
        context.Set<Employee>().AddRange(own, other, foreign);
        var ownType = new LeaveType { TenantId = 10, Code = "CAS", Name = "Casual", MaxDaysPerYear = 10, IsActive = true };
        var foreignType = new LeaveType { TenantId = 20, Code = "FOR", Name = "Foreign", MaxDaysPerYear = 10, IsActive = true };
        context.Set<LeaveType>().AddRange(ownType, foreignType);
        await context.SaveChangesAsync();
        context.Set<EmployeeLeaveApplication>().AddRange(
            Leave(10, own.Id, ownType.Id, "Own leave"),
            Leave(10, other.Id, ownType.Id, "Other user"),
            Leave(20, foreign.Id, foreignType.Id, "Other tenant"));
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetLeaveHistoryAsync();
        result.Success.Should().BeTrue(); result.Data.Should().ContainSingle();
        result.Data![0].LeaveType.Should().Be("Casual"); result.Data[0].Reason.Should().Be("Own leave");
    }

    [Fact]
    public async Task Apply_leave_rejects_when_annual_entitlement_is_exhausted()
    {
        await using var context = CreateContext(out var accessor);
        var employee = Employee(10, 99, "OWN"); context.Set<Employee>().Add(employee);
        var type = new LeaveType { TenantId = 10, Code = "CAS", Name = "Casual", MaxDaysPerYear = 5, IsActive = true };
        context.Set<LeaveType>().Add(type); await context.SaveChangesAsync();
        var year = DateTime.UtcNow.Year;
        context.Set<EmployeeLeaveApplication>().Add(new EmployeeLeaveApplication
        {
            TenantId = 10, EmployeeId = employee.Id, LeaveTypeId = type.Id,
            FromDate = new DateOnly(year, 1, 10), ToDate = new DateOnly(year, 1, 13), TotalDays = 4,
            Reason = "Approved existing leave", State = LeaveState.Approved, ClientRequestId = Guid.NewGuid()
        });
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99)).ApplyLeaveAsync(new EmployeePortalLeaveApplyDto
        {
            LeaveTypeId = type.Id, FromDate = new DateTime(year, 2, 10), ToDate = new DateTime(year, 2, 11),
            Reason = "Need two more days"
        });
        result.Success.Should().BeFalse(); result.StatusCode.Should().Be(400);
        context.Set<EmployeeLeaveApplication>().Count(x => x.TenantId == 10 && x.EmployeeId == employee.Id).Should().Be(1);
    }

    [Fact]
    public async Task Apply_leave_rejects_overlapping_submitted_leave()
    {
        await using var context = CreateContext(out var accessor);
        var employee = Employee(10, 99, "OWN"); context.Set<Employee>().Add(employee);
        var type = new LeaveType { TenantId = 10, Code = "CAS", Name = "Casual", MaxDaysPerYear = 20, IsActive = true };
        context.Set<LeaveType>().Add(type); await context.SaveChangesAsync();
        var year = DateTime.UtcNow.Year;
        context.Set<EmployeeLeaveApplication>().Add(new EmployeeLeaveApplication
        {
            TenantId = 10, EmployeeId = employee.Id, LeaveTypeId = type.Id,
            FromDate = new DateOnly(year, 3, 10), ToDate = new DateOnly(year, 3, 12), TotalDays = 3,
            Reason = "Submitted leave", State = LeaveState.Submitted, ClientRequestId = Guid.NewGuid()
        });
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99)).ApplyLeaveAsync(new EmployeePortalLeaveApplyDto
        {
            LeaveTypeId = type.Id, FromDate = new DateTime(year, 3, 12), ToDate = new DateTime(year, 3, 13),
            Reason = "Overlapping request"
        });
        result.Success.Should().BeFalse(); result.StatusCode.Should().Be(409);
        context.Set<EmployeeLeaveApplication>().Count(x => x.TenantId == 10 && x.EmployeeId == employee.Id).Should().Be(1);
    }

    private static EmployeeAttendance Attendance(long tenant, long employeeId, DateOnly date, AttendanceState state) => new()
    {
        TenantId = tenant, EmployeeId = employeeId, AttendanceDate = date, State = state
    };

    private static EmployeeLeaveApplication Leave(long tenant, long employeeId, long leaveTypeId, string reason) => new()
    {
        TenantId = tenant, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
        FromDate = DateOnly.FromDateTime(DateTime.UtcNow),
        ToDate = DateOnly.FromDateTime(DateTime.UtcNow),
        TotalDays = 1, Reason = reason, State = LeaveState.Submitted, ClientRequestId = Guid.NewGuid()
    };

    private static EduOSDbContext CreateContext(out HttpContextAccessor accessor)
    {
        accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("employee-self-service-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private static void SetTenant(HttpContextAccessor accessor, long tenantId) => accessor.HttpContext!.Items["TenantId"] = tenantId;

    private static EmployeeSelfServiceService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<Employee>(context),
        new GenericRepository<EmployeeAttendance>(context),
        new GenericRepository<EmployeeLeaveApplication>(context),
        new GenericRepository<LeaveType>(context),
        new GenericRepository<EmployeeLeaveEntitlement>(context),
        new GenericRepository<EmployeeLeaveAdjustment>(context),
        new GenericRepository<SalaryStructure>(context),
        new GenericRepository<SalaryStructureLine>(context),
        new GenericRepository<SalaryComponent>(context),
        user, NullLogger<EmployeeSelfServiceService>.Instance);

    private static Employee Employee(long tenant, long userId, string code = "EMP") => new()
    {
        TenantId = tenant, UserId = userId, EmployeeCode = code, FullName = code,
        PersonId = 1, DesignationId = 1, JoiningDate = DateOnly.FromDateTime(DateTime.UtcNow),
        State = EmployeeState.Active
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
