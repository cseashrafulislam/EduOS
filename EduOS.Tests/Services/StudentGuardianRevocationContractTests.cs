using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentGuardianRevocationContractTests
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
    public void Revocation_is_tenant_scoped_versioned_atomic_and_preserves_audit_history()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        service.Should().Contain("UnlinkAsync(")
            .And.Contain("x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId")
            .And.Contain("IgnoreQueryFilters()")
            .And.Contain("VersionMatches(link.RowVersion, request.RowVersion)")
            .And.Contain("link.IsDeleted = true; link.DeletedAt = now; link.DeletedBy = _user.UserId;")
            .And.Contain("Action = \"StudentGuardian.Unlinked\"")
            .And.Contain("await _auditLogs.AddAsync(new AuditLog")
            .And.Contain("await _uow.SaveChangesAsync(token)")
            .And.Contain("System.Data.IsolationLevel.Serializable");
    }

    [Fact]
    public void Revocation_protects_last_primary_and_only_accepts_matching_retries()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        service.Should().Contain("link.IsPrimary && !others.Any(x => x.IsPrimary)")
            .And.Contain("Assign another primary guardian before unlinking this one.")
            .And.Contain("prior?.NewValue == auditValue")
            .And.Contain("x.UserId == _user.UserId && !x.IsDeleted")
            .And.Contain("Relationship was already revoked. Refresh the student.");
        var portal = Read("EduOS.Service", "Services", "Portals", "SelfServicePortalService.cs");
        portal.Should().Contain("guardians.Contains(x.GuardianId) && !x.IsDeleted");
    }

    [Fact]
    public void Revocation_is_authorized_and_available_from_safe_guardian_workspace()
    {
        var dto = Read("EduOS.Core", "DTOs", "Students", "StudentGuardianWorkspaceDtos.cs");
        var contract = Read("EduOS.Core", "Interfaces", "IServices", "IStudentGuardianWorkspaceService.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "guardian-link-edit.js");
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        dto.Should().Contain("class UnlinkStudentGuardianWorkspaceRequestDto")
            .And.Contain("StringLength(500, MinimumLength = 10)");
        contract.Should().Contain("UnlinkAsync(");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]")
            .And.Contain("[RequireModule(\"STUDENT\")]")
            .And.Contain("[AutoValidateAntiforgeryToken]")
            .And.Contain("[HttpPost(\"students/{studentReference:guid}/links/{linkId:long}/unlink\")]")
            .And.Contain("_service.UnlinkAsync(studentReference, linkId, request, ct)");
        js.Should().Contain("rowVersion: guardian.rowVersion")
            .And.Contain("reason: unlinkReason.value.trim()")
            .And.Contain("unlinkForm.reportValidity()")
            .And.Contain("textContent")
            .And.NotContain("innerHTML");
        view.Should().Contain("Revocation preserves history");
    }
}
