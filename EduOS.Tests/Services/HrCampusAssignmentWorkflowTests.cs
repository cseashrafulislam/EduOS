using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
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

    private static HrAdminService Service(string role) => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, new TestUser(role),
        TimeProvider.System, null!, NullLogger<HrAdminService>.Instance);

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
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
