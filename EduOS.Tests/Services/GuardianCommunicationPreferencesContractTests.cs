using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class GuardianCommunicationPreferencesContractTests
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
    public void Guardian_can_only_change_own_tenant_scoped_notification_flags()
    {
        var source = Read("EduOS.Service", "Services", "Students", "GuardianCommunicationPreferenceService.cs");
        source.Should().Contain("guardian.UserId == user.UserId");
        source.Should().Contain("guardian.TenantId == user.TenantId");
        source.Should().Contain("link.TenantId == user.TenantId");
        source.Should().Contain("student.TenantId == user.TenantId");
        source.Should().Contain("!link.IsDeleted && !student.IsDeleted");
        source.Should().Contain("VersionMatches(row.Link.RowVersion, request.RowVersion)");
        source.Should().Contain("IsolationLevel.Serializable");
        source.Should().Contain("db.AuditLogs.Add");
        source.Should().Contain("row.Link.CanReceiveSms = request.CanReceiveSms");
        source.Should().Contain("row.Link.CanReceiveEmail = request.CanReceiveEmail");
        source.Should().NotContain("row.Link.IsPrimary =");
        source.Should().NotContain("row.Link.IsAuthorizedPickup =");
    }

    [Fact]
    public void Guardian_api_is_role_module_and_antiforgery_protected()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "GuardianCommunicationPreferencesController.cs");
        api.Should().Contain("[Authorize(Roles = \"Guardian,Parent\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[ResponseCache(NoStore = true");
        api.Should().Contain("UpdateMineAsync");
    }

    [Fact]
    public void Portal_has_real_preference_controls_and_safe_dom_rendering()
    {
        var view = Read("EduOS.App", "Views", "StudentPortal", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "guardian-preferences.js");
        view.Should().Contain("id=\"guardianPreferences\"");
        view.Should().Contain("guardian-preferences.js");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken: token()");
        js.Should().Contain("rowVersion: item.rowVersion");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("textContent");
        js.Should().NotContain("innerHTML");
    }
}
