using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HrShiftAssignmentWorkflowTests
{
    [Fact]
    public void Shift_history_contract_is_typed_and_authorized_for_tenant_administrators()
    {
        typeof(IHrAdminService).GetMethod(nameof(IHrAdminService.GetShiftAssignmentsAsync))
            .Should().NotBeNull();
        var action = typeof(HrAdminController).GetMethod(nameof(HrAdminController.ShiftAssignments));
        action.Should().NotBeNull();
        action!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Should().Contain(x => x.Roles == "TenantAdmin");
        typeof(HrAdminController).GetMethod(nameof(HrAdminController.AssignShift))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Should().Contain(x => x.Roles == "TenantAdmin");
    }

    [Fact]
    public async Task Non_admin_cannot_read_cross_campus_shift_history()
    {
        var response = await Service("HR").GetShiftAssignmentsAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_pagination_is_rejected_without_database_access()
    {
        var response = await Service("TenantAdmin").GetShiftAssignmentsAsync(Guid.NewGuid(), 0, 20);
        response.StatusCode.Should().Be(400);
    }

    private static HrAdminService Service(string role) => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, new TestUser(role),
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
