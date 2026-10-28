using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentAttendanceAuthorizationContractTests
{
    [Fact]
    public void Historical_roster_endpoint_is_manager_only_no_store_and_read_only()
    {
        var controller = typeof(StudentAttendanceReportsController);
        var roles = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        Assert.Equal("TenantAdmin,Principal,VicePrincipal", roles.Roles);
        Assert.True(Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()).NoStore);
        var action = controller.GetMethod(nameof(StudentAttendanceReportsController.FinalizedSessionRoster))!;
        Assert.Equal("sessions/{sessionId:long}/roster",
            Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), true)
                .Cast<HttpGetAttribute>()).Template);
        Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.NotNull(typeof(EduOS.Core.Interfaces.IServices.IStudentAttendanceReportService)
            .GetMethod("GetFinalizedSessionRosterAsync"));
    }

    [Fact]
    public void Attendance_controller_keeps_role_module_antiforgery_rate_limit_and_no_store_boundaries()
    {
        var controller = typeof(StudentAttendanceController);
        var roleBoundary = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Equal("TenantAdmin,Principal,VicePrincipal,Teacher", roleBoundary.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ATTENDANCE", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Attendance_session_creation_is_protected_post_and_uses_canonical_service()
    {
        var method = typeof(StudentAttendanceController).GetMethod(nameof(StudentAttendanceController.EnsureSession))!;
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Equal("sessions", route.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotNull(typeof(EduOS.Core.Interfaces.IServices.IStudentAttendanceService)
            .GetMethod("EnsureSessionAsync"));
        Assert.Null(typeof(EduOS.Core.DTOs.Attendance.EnsureStudentAttendanceSessionDto).GetProperty("TenantId"));
    }

    [Fact]
    public void Attendance_save_is_post_only_and_not_anonymous()
    {
        var method = typeof(StudentAttendanceController).GetMethods().Single(x => x.Name == nameof(StudentAttendanceController.Save));

        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Paged_roster_keeps_canonical_service_authorization_and_tenant_isolation()
    {
        var method = typeof(StudentAttendanceController).GetMethod(nameof(StudentAttendanceController.GetRosterPage))!;
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Equal("roster-page", route.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotNull(typeof(EduOS.Core.Interfaces.IServices.IStudentAttendanceService)
            .GetMethod("GetRosterPageAsync"));
        Assert.Null(typeof(EduOS.Core.DTOs.Attendance.StudentAttendanceRosterQueryDto).GetProperty("TenantId"));
        var roster = new EduOS.Core.DTOs.Attendance.StudentAttendanceRosterDto();
        Assert.Equal(1, roster.Page);
        Assert.Equal(2000, roster.PageSize);
    }

    [Fact]
    public void Attendance_roster_is_get_only_and_not_anonymous()
    {
        var method = typeof(StudentAttendanceController).GetMethods().Single(x => x.Name == nameof(StudentAttendanceController.GetRoster));

        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
