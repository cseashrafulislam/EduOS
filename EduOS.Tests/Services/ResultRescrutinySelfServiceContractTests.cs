using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResultRescrutinySelfServiceContractTests
{
    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    [Fact]
    public void Linked_portal_requires_actual_student_or_guardian_relation_and_visible_publication()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.RescrutinyPortal.cs");
        service.Should().Contain("student.UserId == _user.UserId");
        service.Should().Contain("adult.UserId == _user.UserId");
        service.Should().Contain("!link.IsDeleted && !adult.IsDeleted && adult.IsActive");
        service.Should().Contain("VisibleToStudent");
        service.Should().Contain("VisibleToGuardian");
        service.Should().Contain("!summary.IsWithheld");
        service.Should().Contain("StudentEnrollmentId");
        service.Should().Contain("HasValidRescrutinyEvidenceAsync");
        service.Should().Contain("Take(pageSize + 1)");
        service.Should().NotContain("IgnoreQueryFilters()");
        service.Should().NotContain("_summaries.Update(");
        var intake = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Rescrutiny.cs");
        intake.Should().Contain("FindLinkedRescrutinyStudentAsync(request.StudentReference");
        intake.Should().Contain("CanViewLinkedRescrutinyPublication");
        intake.Should().Contain("privilegedIntake || !x.IsWithheld");
    }

    [Fact]
    public void Portal_api_and_bilingual_page_use_existing_immutable_application_ledger()
    {
        var api = Read("EduOS.App/Controllers/Api/StudentResultRescrutinyApiController.cs");
        api.Should().Contain("Authorize(Roles = \"Student,Guardian,Parent\")");
        api.Should().Contain("RequireModule(\"EXAM\")");
        api.Should().Contain("AutoValidateAntiforgeryToken");
        api.Should().Contain("studentReference != request.StudentReference");
        api.Should().Contain("SearchLinkedResultRescrutinyAsync");
        var mvc = Read("EduOS.App/Controllers/StudentPortalController.cs");
        mvc.Should().Contain("IActionResult ResultRescrutiny()");
        var view = Read("EduOS.App/Views/StudentPortal/ResultRescrutiny.cshtml");
        view.Should().Contain("CultureInfo.CurrentUICulture");
        view.Should().Contain("learnerRescrutinyForm");
        var js = Read("EduOS.App/wwwroot/js/student-portal/result-rescrutiny.js");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("crypto.randomUUID()");
        js.Should().Contain("replaceChildren");
        js.Should().Contain("encodeURIComponent(student.value)");
        js.Should().NotContain("innerHTML");
    }
}
