using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentSelectorPagingContractTests
{
    private static string Read(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData(nameof(AssessmentAdministrationApiController.SearchSubjectCompletionBatches), "subject-completions/batches/page")]
    [InlineData(nameof(AssessmentAdministrationApiController.SearchCertificateCandidates), "certificates/candidates/page")]
    [InlineData(nameof(AssessmentAdministrationApiController.SearchTranscriptCandidates), "transcripts/candidates/page")]
    public void Authorized_paged_candidate_endpoints_are_exposed(string action, string route)
    {
        var method = typeof(AssessmentAdministrationApiController).GetMethod(action);
        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>().Select(x => x.Template).Should().Contain(route);
    }

    [Fact]
    public void All_three_selectors_page_after_authorization_and_before_materialization()
    {
        typeof(IAssessmentAdministrationService).GetMethod(nameof(IAssessmentAdministrationService.SearchSubjectCompletionBatchesAsync)).Should().NotBeNull();
        typeof(IAssessmentAdministrationService).GetMethod(nameof(IAssessmentAdministrationService.SearchCertificateCandidatesAsync)).Should().NotBeNull();
        typeof(IAssessmentAdministrationService).GetMethod(nameof(IAssessmentAdministrationService.SearchTranscriptCandidatesAsync)).Should().NotBeNull();
        foreach (var file in new[] {
            "AssessmentAdministrationService.SubjectCompletions.cs",
            "AssessmentAdministrationService.CertificateRegister.cs",
            "AssessmentAdministrationService.TranscriptRegister.cs" })
        {
            var source = Read("EduOS.Service/Services/Assessment/" + file);
            source.Should().Contain("CountAsync(ct)");
            source.Should().Contain(".Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)");
            source.Should().Contain("IsRestrictedCampusManager()");
        }
    }

    [Theory]
    [InlineData("subject-completions.js", "subject-completions/batches/page")]
    [InlineData("certificates.js", "certificates/candidates/page")]
    [InlineData("transcripts.js", "transcripts/candidates/page")]
    public void Browser_selectors_use_paged_api_instead_of_legacy_bounded_list(string file, string route)
    {
        var source = Read("EduOS.App/wwwroot/js/assessment/" + file);
        source.Should().Contain(route);
        source.Should().Contain("totalCount");
        source.Should().Contain("Page");
    }
}
