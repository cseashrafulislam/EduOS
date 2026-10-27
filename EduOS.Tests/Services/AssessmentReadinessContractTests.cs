using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentReadinessContractTests
{
    [Fact]
    public void Readiness_endpoint_is_exposed_by_the_canonical_service()
    {
        typeof(IAssessmentAdministrationService).GetMethod(nameof(IAssessmentAdministrationService.GetResultReadinessAsync))
            .Should().NotBeNull();
        var method = typeof(AssessmentAdministrationApiController).GetMethod(
            nameof(AssessmentAdministrationApiController.ResultReadiness));
        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>().Select(x => x.Template).Should().Contain("results/readiness");
        new AssessmentReadinessQueryDto { AssessmentId = 1, AcademicBatchId = 1, Page = 1, PageSize = 50 }
            .PageSize.Should().Be(50);
    }

    [Fact]
    public void Readiness_is_bounded_authorized_and_uses_the_same_elective_rule_as_publication()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Readiness.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("ScopeAsync(request, ct)");
        source.Should().Contain("roster.CountAsync(ct)");
        source.Should().Contain(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)");
        source.Should().Contain("AssessmentSubjectEligibility.RequiresMark");
        source.Should().Contain("MissingRegistration");
        source.Should().Contain("MissingMark");
        source.Should().Contain("Withheld");
        var publication = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        publication.Should().Contain("AssessmentSubjectEligibility.AppliesToEnrollment");
        publication.Should().Contain("AssessmentSubjectEligibility.RequiresMark");
        publication.Should().Contain("Student has no applicable registered assessment subjects.");
    }

    private static string Read(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }
}
