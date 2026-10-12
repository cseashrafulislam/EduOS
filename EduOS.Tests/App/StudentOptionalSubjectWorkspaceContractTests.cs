using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentOptionalSubjectWorkspaceContractTests
{
    [Fact]
    public void Elective_workspace_reuses_canonical_writes_and_enforces_tenant_scoped_reads()
    {
        var service = Read("EduOS.Service", "Services", "Academic", "StudentOptionalSubjectQueryService.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "StudentOptionalSubjectsController.cs");
        var mvc = Read("EduOS.App", "Controllers", "StudentSubjectsController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "optional-subjects.js");
        service.Should().Contain("_user.IsTenantAdmin").And.Contain("x.TenantId == tenant")
            .And.Contain("item.IsOptional").And.Contain("enrollment.AcademicBatchId");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        js.Should().Contain("studentEnrollmentReference").And.Contain("subjectOfferingReference")
            .And.Contain("clientRequestId:requestId").And.Contain("rowVersion:item.rowVersion")
            .And.Contain("RequestVerificationToken");
    }

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
}
