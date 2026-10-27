using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentCampusAuthorizationContractTests
{
    private static string Read(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "EduOS.Service", "Services", "Assessment", name);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void Principal_and_vice_principal_require_active_tenant_and_campus_grants()
    {
        var service = Read("AssessmentAdministrationService.cs");
        service.Should().Contain("IGenericRepository<UserCampusAccess>");
        service.Should().Contain("_user.IsInRole(\"Principal\") || _user.IsInRole(\"VicePrincipal\")");
        service.Should().Contain("access.UserId == _user.UserId && access.IsActive && !access.IsDeleted");
        service.Should().Contain("campus.IsActive && !campus.IsDeleted");
        service.Should().Contain("access.TenantId == tenant && campus.TenantId == tenant");
        service.Should().Contain("AllowedCampusIds(tenant).Contains(x.exam.CampusId)");
        service.Should().Contain("if (exam == null || !await CanAccessCampusAsync(exam.CampusId, ct)) return null;");
        service.Should().Contain("if (CanPublish()) return await CanAccessOfferingAsync(offeringId, ct);");
    }

    [Fact]
    public void Assessment_mutations_and_read_registers_enforce_campus_before_changes_or_paging()
    {
        var setup = Read("AssessmentAdministrationService.Setup.cs");
        setup.Should().Contain("CanAccessCampusAsync(request.CampusId, token)");
        setup.Should().Contain("CanAccessCampusAsync(row.CampusId, token)");
        setup.Should().Contain("CanAccessCampusAsync(exam.CampusId, token)");
        Read("AssessmentAdministrationService.ComponentRegister.cs").Should()
            .Contain("query = query.Where(x => AllowedCampusIds(tenant).Contains(x.exam.CampusId));");
        Read("AssessmentAdministrationService.ScheduleRegister.cs").Should()
            .Contain("CanAccessCampusAsync(scope.CampusId, ct)");
        Read("AssessmentAdministrationService.SubjectCompletions.cs").Should()
            .Contain("CanAccessCampusAsync(batch.CampusId, token)");
        var documents = Read("AssessmentAdministrationService.Documents.cs");
        documents.Should().Contain("CanAccessEnrollmentAsync(replay.StudentEnrollmentId, token)");
        documents.Should().Contain("CanAccessEnrollmentAsync(row.StudentEnrollmentId, token)");
        documents.Should().Contain("CanAccessStudentHistoryAsync(replay.StudentId, token)");
        documents.Should().Contain("CanAccessStudentHistoryAsync(row.StudentId, token)");
        documents.Should().Contain("CanAccessCampusAsync(exam.CampusId, token)");
    }

    [Fact]
    public void Scope_paging_has_authorized_service_and_api_contract()
    {
        typeof(IAssessmentAdministrationService)
            .GetMethod(nameof(IAssessmentAdministrationService.SearchAvailableScopesAsync))
            .Should().NotBeNull();
        var endpoint = typeof(AssessmentAdministrationApiController)
            .GetMethod(nameof(AssessmentAdministrationApiController.SearchScopes));
        endpoint.Should().NotBeNull();
        endpoint!.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>().Single().Template.Should().Be("scopes/page");
        typeof(AssessmentAdministrationApiController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        var source = Read("AssessmentAdministrationService.cs");
        source.Should().Contain("pageSize is < 1 or > 100");
        source.Should().Contain("var total = await q.CountAsync(ct);");
        source.Should().Contain("Skip((int)skip).Take(pageSize)");
    }
}
