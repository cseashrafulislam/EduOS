using EduOS.App.Controllers.Api;
using EduOS.App.Controllers;
using EduOS.Core.DTOs.Learners;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LearnerIdentityUiContractTests
{
    [Fact]
    public void Registration_uses_tenant_scoped_opaque_student_reference()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "LearnerIdentitiesApiController.cs");
        var service = Read("EduOS.Service", "Services", "Learners", "LearnerIdentityService.cs");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("RegisterLearnerIdentityByReferenceRequestDto");
        api.Should().Contain("RegisterOrRequestByReferenceAsync");
        service.Should().Contain("x.TenantId == _currentUser.TenantId && x.PublicId == request.StudentReference");
        service.Should().Contain("x.StatusCode == \"Active\" && !x.IsDeleted");
        service.Should().Contain("return await RegisterOrRequestAsync(new RegisterLearnerIdentityRequestDto");
        typeof(LearnerIdentitiesApiController).GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Should().ContainSingle();
        typeof(LearnerIdentitiesController).GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Should().BeEmpty();
        typeof(RegisterLearnerIdentityByReferenceRequestDto).GetProperty("StudentId").Should().BeNull();
    }

    [Fact]
    public void Registration_view_and_browser_are_reachable_and_do_not_render_private_identifiers()
    {
        var mvc = Read("EduOS.App", "Controllers", "LearnerIdentitiesController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        var view = Read("EduOS.App", "Views", "LearnerIdentities", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "learners", "identity-registration.js");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin,AdmissionOfficer\")]");
        menu.Should().Contain("href=\"/LearnerIdentities/Index\"");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("identityStudentReference");
        view.Should().Contain("identityMembership");
        view.Should().Contain("identityAcademic");
        js.Should().Contain("/api/students?");
        js.Should().Contain("/api/learner-identities/register-or-request");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("referrerPolicy: 'no-referrer'");
        js.Should().Contain("identifier.value = ''");
        js.Should().Contain("textContent");
        js.Should().NotContain("innerHTML");
        js.Should().NotContain("localStorage");
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
