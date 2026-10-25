using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentOptionalSubjectPortalContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Elective_query_enforces_tenant_and_explicit_guardian_relationship()
    {
        var source = Read("EduOS.Service", "Services", "Academic", "StudentOptionalSubjectQueryService.cs");
        source.Should().Contain("student.UserId != _user.UserId");
        source.Should().Contain("link.StudentId == student.Id && guardian.UserId == _user.UserId");
        source.Should().Contain("guardian.IsActive");
        source.Should().Contain("x.TenantId == tenant && x.PublicId == studentReference");
        source.Should().Contain("Student not found.");
        source.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Elective_api_and_mvc_require_academic_module_and_authenticated_roles()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentOptionalSubjectsController.cs");
        var mvc = Read("EduOS.App", "Controllers", "StudentSubjectsController.cs");
        api.Should().Contain("[RequireModule(\"ACADEMIC\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("Student,Guardian,Parent");
        mvc.Should().Contain("[RequireModule(\"ACADEMIC\")]");
        mvc.Should().Contain("Student,Guardian,Parent");
    }

    [Fact]
    public void Learner_view_hides_admin_decisions_and_uses_safe_dom_and_csrf()
    {
        var view = Read("EduOS.App", "Views", "StudentSubjects", "Index.cshtml");
        var script = Read("EduOS.App", "wwwroot", "js", "students", "optional-subjects.js");
        var navigation = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        view.Should().Contain("data-can-manage");
        view.Should().Contain("subjectsLinkedStudents");
        view.Should().Contain("@Html.AntiForgeryToken()");
        script.Should().Contain("if(canManage&&(item.state===1");
        script.Should().Contain("if(!canManage)return;");
        script.Should().Contain("'/api/portal/students'");
        script.Should().Contain("RequestVerificationToken:token");
        script.Should().Contain("textContent");
        script.Should().NotContain("innerHTML");
        navigation.Should().Contain("href=\"/StudentSubjects/Index\"");
    }
}
