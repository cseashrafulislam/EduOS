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
public class EmployeeLeaveBalanceTests
{
    [Fact]
    public async Task Leave_balances_are_scoped_to_current_employee_tenant_and_year()
    {
        await using var context = CreateContext(out var accessor);
        var own = Employee(10, 99); var another = Employee(10, 100, "OTHER"); var foreign = Employee(20, 99, "FOREIGN");
        context.Set<Employee>().AddRange(own, another, foreign);
        var casual = new LeaveType { TenantId = 10, Code = "CAS", Name = "Casual", MaxDaysPerYear = 10, IsActive = true };
        var sick = new LeaveType { TenantId = 10, Code = "SICK", Name = "Sick", MaxDaysPerYear = 8, IsActive = true };
        var otherTenant = new LeaveType { TenantId = 20, Code = "OTHER", Name = "Other", MaxDaysPerYear = 99, IsActive = true };
        context.Set<LeaveType>().AddRange(casual, sick, otherTenant);
        await context.SaveChangesAsync();
        var year = DateTime.UtcNow.Year;
        context.Set<EmployeeLeaveApplication>().AddRange(
            Leave(10, own.Id, casual.Id, year, 3, LeaveState.Approved),
            Leave(10, own.Id, casual.Id, year, 2, LeaveState.Submitted),
            Leave(10, another.Id, casual.Id, year, 4, LeaveState.Approved),
            Leave(10, own.Id, casual.Id, year - 1, 5, LeaveState.Approved),
            Leave(20, foreign.Id, otherTenant.Id, year, 7, LeaveState.Approved));
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

    private static EmployeeLeaveApplication Leave(long tenant, long employeeId, long typeId, int year, int days, LeaveState state) => new()
    {
        TenantId = tenant, EmployeeId = employeeId, LeaveTypeId = typeId,
        FromDate = new DateOnly(year, 4, 1), ToDate = new DateOnly(year, 4, days),
        TotalDays = days, State = state, Reason = "Test leave", ClientRequestId = Guid.NewGuid()
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
