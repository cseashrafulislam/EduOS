using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentGuardianRelationshipWorkspaceContractTests
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
    public void Guardian_relationship_updates_are_tenant_scoped_versioned_and_atomic()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentGuardianWorkspaceService.cs");
        service.Should().Contain("x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId");
        service.Should().Contain("VersionMatches(link.RowVersion, request.RowVersion)");
        service.Should().Contain("Assign another primary guardian first.");
        service.Should().Contain("System.Data.IsolationLevel.Serializable");
        service.Should().Contain("link.CanReceiveSms = request.CanReceiveSms");
        service.Should().Contain("link.CanReceiveEmail = request.CanReceiveEmail");
        service.Should().Contain("link.IsAuthorizedPickup = request.IsAuthorizedPickup");
    }

    [Fact]
    public void Guardian_api_requires_admin_student_module_and_antiforgery()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentGuardiansController.cs");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("UpdateLinkAsync");
        api.Should().Contain("students/{studentReference:guid}/links/{linkId:long}");
        var navigation = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        navigation.Should().Contain("href=\"/StudentGuardians/Index\"");
    }

    [Fact]
    public void Guardian_workspace_exposes_versioned_edit_and_contact_permissions_without_html_injection()
    {
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        var script = Read("EduOS.App", "wwwroot", "js", "students", "guardian-link-edit.js");
        var save = Read("EduOS.App", "wwwroot", "js", "students", "guardian-save.js");
        var entry = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("guardianSms");
        view.Should().Contain("guardianPickup");
        script.Should().Contain("rowVersion: guardian.rowVersion");
        script.Should().Contain("'/links/'");
        script.Should().Contain("api(url, 'PUT'");
        script.Should().Contain("textContent");
        script.Should().NotContain("innerHTML");
        save.Should().Contain("clientRequestId: state.requestId");
        save.Should().Contain("canReceiveSms: $('guardianSms').checked");
        entry.Should().Contain("RequestVerificationToken: token");
        entry.Should().Contain("credentials: 'same-origin'");
    }
}
