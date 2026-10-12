using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentGuardianWorkspaceContractTests
{
    [Fact]
    public void Guardian_workspace_enforces_tenant_admin_and_transactional_links()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        var mvc = Read("EduOS.App", "Controllers", "StudentGuardiansController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        var save = Read("EduOS.App", "wwwroot", "js", "students", "guardian-save.js");
        service.Should().Contain("_user.IsTenantAdmin").And.Contain("x.TenantId == tenant")
            .And.Contain("IsolationLevel.Serializable").And.Contain("VersionMatches")
            .And.Contain("request.ClientRequestId").And.Contain("link.IsPrimary = false");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]").And.Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        js.Should().Contain("RequestVerificationToken").And.Contain("textContent");
        save.Should().Contain("studentRowVersion").And.Contain("clientRequestId");
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
