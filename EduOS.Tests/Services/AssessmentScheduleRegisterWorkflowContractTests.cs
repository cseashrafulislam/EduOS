using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentScheduleRegisterWorkflowContractTests
{
    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Register_is_tenant_and_campus_scoped_with_bounded_pagination()
    {
        var service = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.ScheduleRegister.cs");
        service.Should().Contain("if (!CanPublish())");
        service.Should().Contain("schedule.TenantId == tenant && subject.TenantId == tenant");
        service.Should().Contain("exam.CampusId == batch.CampusId");
        service.Should().Contain("request.PageSize is < 1 or > 100");
        service.Should().Contain("Take(request.PageSize)");
        service.Should().Contain("MapScheduleAsync");
    }

    [Fact]
    public void Save_enforces_dates_campus_room_conflicts_and_concurrency()
    {
        var setup = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.Setup.cs");
        setup.Should().Contain("request.AssessmentDate < exam.StartDate");
        setup.Should().Contain("batch.CampusId != exam.CampusId");
        setup.Should().Contain("Room already booked for an overlapping assessment.");
        setup.Should().Contain("scheduled.StartTime < request.EndTime");
        setup.Should().Contain("Matches(row.RowVersion, request.RowVersion)");
    }

    [Fact]
    public void Api_mvc_and_navigation_use_exam_manager_roles()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "ExamWorkflowController.cs");
        var mvc = Read("EduOS.App", "Controllers", "ExamOperationsController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        api.Should().Contain("[HttpGet(\"schedules\")]");
        api.Should().Contain("[HttpGet(\"schedules/rooms\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult AssessmentSchedules() => View()");
        menu.Should().Contain("href=\"/ExamOperations/AssessmentSchedules\"");
    }

    [Fact]
    public void Browser_uses_antiforgery_concurrency_safe_rendering_and_export()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "exams", "assessment-schedules.js");
        var view = Read("EduOS.App", "Views", "ExamOperations", "AssessmentSchedules.cshtml");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("rowVersion: state.rowVersion");
        js.Should().Contain("td.textContent");
        js.Should().Contain("URL.createObjectURL");
        js.Should().Contain("Check the register before retrying");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"scheduleRows\"");
    }
}
