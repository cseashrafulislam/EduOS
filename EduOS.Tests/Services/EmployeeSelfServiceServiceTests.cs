using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Portals;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class EmployeeSelfServiceServiceTests
{
    [Fact]
    public async Task Attendance_returns_only_current_users_rows_in_current_tenant()
    {
        await using var context = CreateContext();
        var own = Employee(10, 99, "OWN-1");
        var other = Employee(10, 100, "OTHER-1");
        var otherTenant = Employee(20, 99, "OTHER-TENANT");
        context.Employees.AddRange(own, other, otherTenant);
        await context.SaveChangesAsync();
        context.EmployeeAttendances.AddRange(
            Attendance(10, own.Id, DateTime.UtcNow.Date.AddDays(-1), "Present"),
            Attendance(10, other.Id, DateTime.UtcNow.Date.AddDays(-1), "Absent"),
            Attendance(20, otherTenant.Id, DateTime.UtcNow.Date.AddDays(-1), "Late"));
        await context.SaveChangesAsync();

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].Status.Should().Be("Present");
    }

    [Fact]
    public async Task Attendance_rejects_inactive_employee_link()
    {
        await using var context = CreateContext();
        var employee = Employee(10, 99, "INACTIVE");
        employee.IsActive = false;
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var result = await CreateService(context, new TestCurrentUser(10, 99)).GetAttendanceAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Attendance_rejects_ranges_longer_than_one_year()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new TestCurrentUser(10, 99));

        var result = await service.GetAttendanceAsync(DateTime.UtcNow.Date.AddDays(-367), DateTime.UtcNow.Date);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private static EduOSDbContext CreateContext() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"employee-portal-{Guid.NewGuid():N}").Options);

    private static EmployeeSelfServiceService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Employee>(context),
        new GenericRepository<EmployeeAttendance>(context),
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
