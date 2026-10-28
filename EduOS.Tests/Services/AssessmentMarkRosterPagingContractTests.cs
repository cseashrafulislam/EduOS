using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkRosterPagingContractTests
{
    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    [Fact]
    public void Marks_register_exposes_paged_scoped_api()
    {
        typeof(IAssessmentAdministrationService).GetMethod(nameof(IAssessmentAdministrationService.SearchMarkRosterAsync))
            .Should().NotBeNull();
        var action = typeof(AssessmentAdministrationApiController)
            .GetMethod(nameof(AssessmentAdministrationApiController.SearchMarkRoster));
        action.Should().NotBeNull();
        action!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()
            .Select(x => x.Template).Should().Contain("mark-roster/page");
        new AssessmentMarkRosterPageQueryDto().PageSize.Should().Be(50);
    }

    [Fact]
    public void Paged_read_enforces_tenant_assignment_and_sql_pagination()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkRosterPaging.cs");
        source.Should().Contain("ScopeAsync(request, ct)");
        source.Should().Contain("CanEditOfferingAsync(offering.Id, ct)");
        source.Should().Contain("registration.TenantId == tenant && enrollment.TenantId == tenant");
        source.Should().Contain("student.TenantId == tenant");
        source.Should().Contain("registration.State == SubjectRegistrationState.Approved");
        source.Should().Contain("CountAsync(ct)");
        source.Should().Contain(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct)");
        source.Should().Contain("ids.Contains(x.StudentSubjectRegistrationId)");
        source.Should().Contain("Duplicate marks require reconciliation.");
        source.Should().Contain("request.PageSize is < 1 or > 100");
    }

    [Fact]
    public void Marks_write_validates_submitted_ids_in_sql_not_legacy_capped_roster()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        var start = source.IndexOf("public async Task<ApiResponse<bool>> SaveMarksRegisterAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public Task<ApiResponse<AssessmentResultSheetDto>> PreviewResultsAsync", start, StringComparison.Ordinal);
        var write = source[start..end];
        write.Should().Contain("ids.Contains(registration.Id)");
        write.Should().Contain("eligibleIds.Count != ids.Length");
        write.Should().Contain("registration.State == SubjectRegistrationState.Approved");
        write.Should().Contain("Matches(row.RowVersion, item.RowVersion)");
        write.Should().NotContain("EligibleAsync(offering.Id, offering.AcademicBatchId");
    }
}
