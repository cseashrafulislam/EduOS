using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentAttendanceCorrectionHistoryContractTests
{
    [Fact]
    public void Correction_history_is_a_manager_only_get_under_the_existing_module_boundary()
    {
        var controller = typeof(StudentAttendanceController);
        var method = controller.GetMethod(nameof(StudentAttendanceController.GetCorrections))!;
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        var managerRole = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("{attendanceId:long}/corrections", route.Template);
        Assert.Equal("TenantAdmin,Principal,VicePrincipal", managerRole.Roles);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true));
        Assert.NotNull(typeof(IStudentAttendanceService).GetMethod(nameof(IStudentAttendanceService.GetCorrectionsAsync)));
    }

    [Fact]
    public void Correction_history_uses_canonical_audit_and_historical_enrollment_identity()
    {
        Assert.Equal(typeof(long), typeof(StudentAttendanceAdjustment).GetProperty("StudentAttendanceId")!.PropertyType);
        Assert.NotNull(typeof(StudentAttendanceAdjustment).GetProperty("PreviousState"));
        Assert.NotNull(typeof(StudentAttendanceAdjustment).GetProperty("NewState"));
        Assert.NotNull(typeof(StudentAttendanceAdjustment).GetProperty("ChangedByUserId"));
        Assert.Null(typeof(StudentAttendanceAdjustmentDto).GetProperty("TenantId"));
        Assert.Null(typeof(StudentAttendanceAdjustmentDto).GetProperty("StudentCurrentEnrollmentId"));
    }

    [Fact]
    public void Correction_history_query_has_bounded_paging_defaults()
    {
        var method = typeof(IStudentAttendanceService).GetMethod(nameof(IStudentAttendanceService.GetCorrectionsAsync))!;
        var args = method.GetParameters();
        Assert.Equal(1, args[1].DefaultValue);
        Assert.Equal(20, args[2].DefaultValue);
        Assert.Equal(typeof(long), args[0].ParameterType);
    }
}
