using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentGuardianReuseContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Existing_guardian_link_reuses_canonical_guardian_and_guards_tenant_and_concurrency()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        var entity = Read("EduOS.Core", "Entities", "Students", "StudentEntities.cs");
        service.Should().Contain("LinkExistingAsync(")
            .And.Contain("x.TenantId == tenant && x.PublicId == request.GuardianReference && x.IsActive && !x.IsDeleted")
            .And.Contain("x.TenantId == tenant && x.StudentId == student.Id && x.GuardianId == guardian.Id")
            .And.Contain("VersionMatches(student.RowVersion, request.StudentRowVersion)")
            .And.Contain("System.Data.IsolationLevel.Serializable")
            .And.Contain("existing.IsEmergencyContact == request.IsEmergencyContact")
            .And.Contain("existing.IsFinancialContact == request.IsFinancialContact")
            .And.Contain("Guardian is already linked or was previously removed");
        entity.Should().Contain("public long GuardianId { get; set; }");
        var context = Read("EduOS.Persistence", "Context", "EduOSDbContext.cs");
        context.Should().Contain("UniqueTenant<StudentGuardian>(modelBuilder, nameof(StudentGuardian.StudentId), nameof(StudentGuardian.GuardianId))");
    }

    [Fact]
    public void Existing_guardian_workflow_has_authorized_api_and_safe_sibling_ui()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        var dto = Read("EduOS.Core", "DTOs", "Students", "StudentGuardianWorkspaceDtos.cs");
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        var entry = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        var reuse = Read("EduOS.App", "wwwroot", "js", "students", "guardian-reuse.js");
        var edit = Read("EduOS.App", "wwwroot", "js", "students", "guardian-link-edit.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]")
            .And.Contain("[RequireModule(\"STUDENT\")]")
            .And.Contain("[AutoValidateAntiforgeryToken]")
            .And.Contain("[HttpPost(\"existing\")]")
            .And.Contain("_service.LinkExistingAsync(request, ct)");
        dto.Should().Contain("class LinkExistingStudentGuardianRequestDto");
        view.Should().Contain("id=\"guardianReuseForm\"").And.Contain("id=\"guardianExistingReference\"");
        entry.Should().Contain("setupGuardianReuse").And.Contain("RequestVerificationToken: token");
        reuse.Should().Contain("studentRowVersion: state.student.studentRowVersion")
            .And.Contain("guardianReference,")
            .And.Contain("'/api/student-guardians/existing'")
            .And.NotContain("innerHTML");
        edit.Should().Contain("guardian.guardianReference").And.Contain("textContent");
    }
}
