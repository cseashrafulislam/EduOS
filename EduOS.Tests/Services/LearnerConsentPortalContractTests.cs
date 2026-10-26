using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LearnerConsentPortalContractTests
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
    public void Consent_portal_supports_real_approve_deny_and_revoke_actions()
    {
        var view = Read("EduOS.App", "Views", "StudentPortal", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "consents.js");
        view.Should().Contain("id=\"learnerConsents\"");
        view.Should().Contain("learnerConsentPending");
        view.Should().Contain("learnerConsentGrants");
        view.Should().Contain("student-portal/consents.js");
        js.Should().Contain("/api/learner-consents/pending");
        js.Should().Contain("/api/learner-consents/grants");
        js.Should().Contain("'/decision'");
        js.Should().Contain("'/revoke'");
        js.Should().Contain("JSON.stringify({ decision })");
        js.Should().Contain("action('Approve'");
        js.Should().Contain("action('Deny'");
        js.Should().Contain("action('Revoke access'");
    }

    [Fact]
    public void Consent_portal_enforces_antiforgery_and_safe_text_rendering()
    {
        var view = Read("EduOS.App", "Views", "StudentPortal", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "consents.js");
        var api = Read("EduOS.App", "Controllers", "Api", "LearnerConsentsController.cs");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("referrerPolicy: 'no-referrer'");
        js.Should().Contain("window.confirm");
        js.Should().Contain("textContent");
        js.Should().NotContain("innerHTML");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[ResponseCache(NoStore = true");
        api.Should().Contain("[EnableRateLimiting(\"LearnerIdentityPolicy\")]");
    }

    [Fact]
    public void Guardian_role_requires_both_api_authorization_and_linked_person_ownership()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "LearnerConsentsController.cs");
        var service = Read("EduOS.Service", "Services", "Learners", "LearnerConsentService.cs");
        var repository = Read("EduOS.Persistence", "Repositories", "LearnerConsentRepository.cs");
        api.Should().Contain("[Authorize(Roles = \"Student,Parent,Guardian\")]");
        service.Should().Contain("_currentUser.IsInRole(\"Guardian\")");
        repository.Should().Contain("UserControlsPersonAsync");
        repository.Should().Contain("guardian.UserId == userId && guardian.IsActive");
        repository.Should().Contain("personIds.Contains(x.PersonId)");
        repository.Should().Contain("return request != null && await UserControlsPersonAsync");
    }
}
