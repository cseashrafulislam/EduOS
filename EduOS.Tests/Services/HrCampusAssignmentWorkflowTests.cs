using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HrCampusAssignmentWorkflowTests
{
    [Fact]
    public void Campus_history_and_write_contracts_require_tenant_admin()
    {
        typeof(IHrAdminService).GetMethod(nameof(IHrAdminService.GetCampusAssignmentsAsync))
            .Should().NotBeNull();
        foreach (var name in new[] { nameof(HrAdminController.CampusAssignments), nameof(HrAdminController.AssignCampus) })
        {
            var action = typeof(HrAdminController).GetMethod(name);
            action.Should().NotBeNull();
            action!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Should().Contain(x => x.Roles == "TenantAdmin");
        }
    }

    [Fact]
    public async Task Non_admin_cannot_read_cross_campus_assignment_history()
    {
        var response = await Service("HR").GetCampusAssignmentsAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_pagination_is_rejected_before_database_access()
    {
        var response = await Service("TenantAdmin").GetCampusAssignmentsAsync(Guid.NewGuid(), 0, 20);
        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Non_admin_cannot_write_cross_campus_assignment()
    {
        var response = await Service("HR").AssignCampusAsync(new EduOS.Core.DTOs.HR.AssignEmployeeCampusRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EmployeeReference = Guid.NewGuid(),
            CampusId = 1, EffectiveFrom = new DateOnly(2026, 10, 26)
        });
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Non_admin_cannot_register_unassigned_cross_campus_employee()
    {
        var response = await Service("HR").CreateEmployeeAsync(new EduOS.Core.DTOs.HR.CreateEmployeeRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EmployeeCode = "EMP-1234",
            FullName = "Test employee", DesignationId = 1,
            JoiningDate = new DateOnly(2026, 10, 28)
        });
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public void Receipt_enforces_tenant_unique_requests_and_tenant_scoped_assignment_reference()
    {
        using var db = Context(41);
        var receipt = db.Model.FindEntityType(typeof(EmployeeCampusAssignmentReceipt));
        receipt.Should().NotBeNull();
        receipt!.GetIndexes().Should().ContainSingle(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "ClientRequestId" }));
        receipt.GetForeignKeys().Should().ContainSingle(x =>
            x.PrincipalEntityType.ClrType == typeof(EmployeeCampusAssignment) &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "EmployeeCampusAssignmentId" }));
    }

    [Fact]
    public async Task Assignment_request_replays_without_extra_writes_even_after_original_dates_change()
    {
        await using var db = Context(41);
        var employee = new Employee
        {
            Id = 401, TenantId = 41, PersonId = 400, DesignationId = 11,
            PublicId = Guid.NewGuid(), FullName = "Campus employee", EmployeeCode = "HR-401",
            JoiningDate = new DateOnly(2026, 1, 1)
        };
        db.Employees.Add(employee);
        db.Campuses.AddRange(
            new Campus { Id = 501, TenantId = 41, Name = "North", Code = "NORTH", IsActive = true },
            new Campus { Id = 502, TenantId = 41, Name = "South", Code = "SOUTH", IsActive = true });
        await db.SaveChangesAsync();
        var service = FullService(db, 41);
        var first = new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EmployeeReference = employee.PublicId, CampusId = 501,
            EffectiveFrom = new DateOnly(2026, 11, 1), IsPrimary = true
        };
        var created = await service.AssignCampusAsync(first);
        created.Success.Should().BeTrue(created.Message);
        var firstId = created.Data!.Id;
        var duplicate = await service.AssignCampusAsync(first);
        duplicate.Success.Should().BeTrue(duplicate.Message);
        duplicate.Data!.Id.Should().Be(firstId);
        db.EmployeeCampusAssignmentReceipts.Count().Should().Be(1);
        db.EmployeeCampusAssignments.Count().Should().Be(1);

        var reassignment = await service.AssignCampusAsync(new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EmployeeReference = employee.PublicId, CampusId = 502,
            EffectiveFrom = new DateOnly(2026, 11, 10), IsPrimary = true
        });
        reassignment.Success.Should().BeTrue(reassignment.Message);
        var original = await db.EmployeeCampusAssignments.AsNoTracking().SingleAsync(x => x.Id == firstId);
        original.EffectiveTo.Should().Be(new DateOnly(2026, 11, 9));

        db.Campuses.Local.Single(x => x.Id == 501).IsActive = false;
        await db.SaveChangesAsync();
        var lateReplay = await service.AssignCampusAsync(first);
        lateReplay.Success.Should().BeTrue(lateReplay.Message);
        lateReplay.Data!.Id.Should().Be(firstId);
        lateReplay.Data.EffectiveTo.Should().BeNull("request receipt keeps the original date range");
        db.EmployeeCampusAssignments.Count().Should().Be(2);
        db.EmployeeCampusAssignmentReceipts.Count().Should().Be(2);

        var changedPayload = await service.AssignCampusAsync(new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = first.ClientRequestId, EmployeeReference = first.EmployeeReference,
            CampusId = 501, EffectiveFrom = first.EffectiveFrom, IsPrimary = false
        });
        changedPayload.StatusCode.Should().Be(409);
        db.EmployeeCampusAssignmentReceipts.Count().Should().Be(2);
    }

    [Fact]
    public async Task Reusing_a_request_id_in_another_tenant_does_not_read_original_tenant_receipt()
    {
        await using var db = Context(41, out var accessor);
        var firstEmployee = new Employee
        {
            Id = 601, TenantId = 41, PublicId = Guid.NewGuid(), PersonId = 11,
            DesignationId = 11, EmployeeCode = "TENANT-41", FullName = "First",
            JoiningDate = new DateOnly(2026, 1, 1)
        };
        var otherEmployee = new Employee
        {
            Id = 602, TenantId = 42, PublicId = Guid.NewGuid(), PersonId = 12,
            DesignationId = 12, EmployeeCode = "TENANT-42", FullName = "Second",
            JoiningDate = new DateOnly(2026, 1, 1)
        };
        db.Employees.AddRange(firstEmployee, otherEmployee);
        db.Campuses.AddRange(
            new Campus { Id = 701, TenantId = 41, Name = "First", Code = "T41" },
            new Campus { Id = 702, TenantId = 42, Name = "Second", Code = "T42" });
        await db.SaveChangesAsync();
        var requestId = Guid.NewGuid();
        var first = await FullService(db, 41).AssignCampusAsync(new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = requestId, EmployeeReference = firstEmployee.PublicId,
            CampusId = 701, EffectiveFrom = new DateOnly(2026, 11, 1)
        });
        first.Success.Should().BeTrue(first.Message);

        accessor.HttpContext!.Items["TenantId"] = 42L;
        var second = await FullService(db, 42).AssignCampusAsync(new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = requestId, EmployeeReference = otherEmployee.PublicId,
            CampusId = 702, EffectiveFrom = new DateOnly(2026, 11, 1)
        });
        second.Success.Should().BeTrue(second.Message);
        db.EmployeeCampusAssignmentReceipts.IgnoreQueryFilters()
            .Count(x => x.ClientRequestId == requestId).Should().Be(2);
    }

    private static EduOSDbContext Context(long tenantId) => Context(tenantId, out _);

    private static EduOSDbContext Context(long tenantId, out HttpContextAccessor accessor)
    {
        accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hr-campus-receipts-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    internal static HrAdminService FullService(EduOSDbContext db, long tenantId) => new(
        new GenericRepository<Employee>(db), new GenericRepository<Designation>(db),
        new GenericRepository<OrganizationUnit>(db), new GenericRepository<EmployeeLeaveApplication>(db),
        new GenericRepository<LeaveType>(db), new GenericRepository<WorkShift>(db),
        new GenericRepository<EmployeeShiftAssignment>(db), new GenericRepository<EmployeeCampusAssignment>(db),
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<EmployeeAssignmentHistory>(db),
        new GenericRepository<EmployeeBankAccount>(db), new GenericRepository<Campus>(db),
        new GenericRepository<Person>(db), new GenericRepository<TenantMembership>(db),
        new GenericRepository<ApplicationUser>(db), new GenericRepository<Student>(db),
        new GenericRepository<Guardian>(db), db, new TestUser("TenantAdmin", tenantId),
        TimeProvider.System, null!, NullLogger<HrAdminService>.Instance, db);

    private static HrAdminService Service(string role) => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, new TestUser(role),
        TimeProvider.System, null!, NullLogger<HrAdminService>.Instance, null!);

    private sealed class TestUser(string role, long tenantId = 41) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
