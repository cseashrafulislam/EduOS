using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentGuardianReactivationContractTests
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
    public void Reactivation_reuses_soft_deleted_relationship_and_enforces_tenant_version_and_transaction()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        service.Should().Contain("ReactivateAsync(")
            .And.Contain("IgnoreQueryFilters().FirstOrDefaultAsync(x =>")
            .And.Contain("x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId")
            .And.Contain("VersionMatches(link.RowVersion, permissions.RowVersion)")
            .And.Contain("link.IsDeleted = false; link.DeletedAt = null; link.DeletedBy = null;")
            .And.Contain("x.TenantId == tenant && x.Id == link.GuardianId && x.IsActive && !x.IsDeleted")
            .And.Contain("System.Data.IsolationLevel.Serializable");
        service.Should().NotContain("new StudentGuardian {\n                TenantId = tenant, StudentId = student.Id, GuardianId = link.GuardianId");
    }

    [Fact]
    public void Reactivation_requires_new_permissions_and_preserves_audit_and_idempotency()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        var dto = Read("EduOS.Core", "DTOs", "Students", "StudentGuardianWorkspaceDtos.cs");
        dto.Should().Contain("class ReactivateStudentGuardianWorkspaceRequestDto")
            .And.Contain("UpdateStudentGuardianWorkspaceLinkDto Relationship")
            .And.Contain("StringLength(500, MinimumLength = 10)")
            .And.Contain("RevokedGuardians");
        service.Should().Contain("permissions.IsPrimary || activeLinks.Count == 0")
            .And.Contain("activeLinks.Count >= 100")
            .And.Contain("Action = \"StudentGuardian.Reactivated\"")
            .And.Contain("OldValue = oldValue, NewValue = auditValue")
            .And.Contain("prior?.NewValue == auditValue")
            .And.Contain("link.IsFinancialContact = permissions.IsFinancialContact")
            .And.Contain("link.IsEmergencyContact = permissions.IsEmergencyContact");
    }

    [Fact]
    public void Reactivation_has_authorized_api_and_explicit_admin_ui_without_html_injection()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        var entry = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        var script = Read("EduOS.App", "wwwroot", "js", "students", "guardian-reactivation.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]")
            .And.Contain("[RequireModule(\"STUDENT\")]")
            .And.Contain("[AutoValidateAntiforgeryToken]")
            .And.Contain("[HttpPost(\"students/{studentReference:guid}/links/{linkId:long}/reactivate\")]")
            .And.Contain("_service.ReactivateAsync(studentReference, linkId, request, ct)");
        view.Should().Contain("id=\"guardianRevokedLinks\"");
        entry.Should().Contain("renderRevokedGuardianLinks");
        script.Should().Contain("rowVersion: guardian.rowVersion")
            .And.Contain("reason: reason.value.trim()")
            .And.Contain("form.reportValidity()")
            .And.Contain("textContent")
            .And.NotContain("innerHTML");
    }
}
