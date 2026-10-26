using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentGuardianRevokedPagingContractTests
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
    public void Revoked_relationships_have_bounded_tenant_scoped_paging_without_restoring_access()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        var contract = Read("EduOS.Core", "Interfaces", "IServices", "IStudentGuardianWorkspaceService.cs");
        service.Should().Contain("GetRevokedAsync(")
            .And.Contain("pageSize is < 1 or > 100")
            .And.Contain("IgnoreQueryFilters().AsNoTracking()")
            .And.Contain("link.TenantId == tenant && link.StudentId == studentId && link.IsDeleted")
            .And.Contain("guardian.TenantId == tenant && !guardian.IsDeleted && guardian.IsActive")
            .And.Contain("OrderByDescending(x => x.Link.DeletedAt).ThenByDescending(x => x.Link.Id)")
            .And.Contain("Skip((int)offset).Take(pageSize)");
        contract.Should().Contain("GetRevokedAsync(");
    }

    [Fact]
    public void Authorized_api_and_safe_reactivation_ui_expose_older_revoked_links()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        var entry = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        var pager = Read("EduOS.App", "wwwroot", "js", "students", "guardian-revoked-paging.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]")
            .And.Contain("[RequireModule(\"STUDENT\")]")
            .And.Contain("[HttpGet(\"students/{studentReference:guid}/revoked\")]");
        view.Should().Contain("guardianRevokedPrev").And.Contain("guardianRevokedNext");
        entry.Should().Contain("setupRevokedGuardianPaging");
        pager.Should().Contain("requestSequence")
            .And.Contain("encodeURIComponent(reference)")
            .And.Contain("renderRevokedGuardianLinks")
            .And.Contain("textContent")
            .And.NotContain("innerHTML");
    }
}
