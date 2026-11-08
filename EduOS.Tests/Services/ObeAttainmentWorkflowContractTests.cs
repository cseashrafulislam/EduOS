using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Research;
using EduOS.Service.Services.AssessmentAdministration;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ObeAttainmentWorkflowContractTests
{
    [Fact]
    public void Obe_api_requires_module_authorization_csrf_and_no_cache()
    {
        var attributes = typeof(ObeAttainmentApiController).GetCustomAttributes(true);
        Assert.Contains(attributes, x => x is RequireModuleAttribute m && m.Policy == "EduOSModule:ACCREDITATION");
        Assert.Contains(attributes, x => x is AutoValidateAntiforgeryTokenAttribute);
        Assert.Contains(attributes, x => x is ResponseCacheAttribute a && a.NoStore);
        var source = Read("EduOS.App", "Controllers", "Api", "ObeAttainmentApiController.cs");
        Assert.Contains("AuthSessionRegistry.ToPublicReference", source);
        Assert.Contains("X-EduOS-Tenant-Id", source);
        Assert.Contains("StateCode != \"Approved\"", source);
        Assert.Contains("Safe(clo.CourseOutcomeCode)", source);
    }

    [Fact]
    public void Schema_uses_immutable_versioned_policy_tenant_foreign_keys_and_exactly_one_review()
    {
        var migration = Read("EduOS.Persistence", "Migrations", "20261108090000_ObePublishedAttainment.cs");
        foreach (var table in new[] { "ObePolicies", "ObeCourseOutcomes", "ObePloContributions", "ObeAttainmentReports" })
            Assert.Contains("CreateTable(\"" + table + "\"", migration);
        Assert.Contains("FK_ObePolicies_Program", migration);
        Assert.Contains("FK_ObePloContributions_PLO", migration);
        Assert.Contains("FK_ObeAttainmentReports_Publication", migration);
        Assert.Contains("decimal(9,4)", migration);
        Assert.Contains("UX_ObeAttainmentReports_Submit", migration);
        Assert.Contains("UX_ObeAttainmentReports_Review", migration);
        Assert.Contains("TR_ObeAttainmentReports_Freeze", migration);
        Assert.Contains("TR_ObePolicies_Immutable", migration);
        Assert.Contains("SubmittedByUserId", migration);
    }

    [Fact]
    public void Frozen_rubric_migration_and_canonical_publication_capture_prevent_live_mark_reinterpretation()
    {
        var migration = Read("EduOS.Persistence", "Migrations",
            "20261108093000_ObePublishedComponentEvidence.cs");
        Assert.Contains("ObePublishedComponentRegisters", migration);
        Assert.Contains("ObePublishedComponentEvidence", migration);
        Assert.Contains("TR_ObePublishedComponentEvidence_Immutable", migration);
        Assert.Contains("TR_ObePublishedComponentRegisters_Immutable", migration);
        Assert.Contains("UX_ObeCourseOutcomes_CourseComponent", migration);
        Assert.Contains("FK_ObePublishedComponentEvidence_Publication", migration);
        Assert.Contains("decimal(18,2)", migration);
        var capture = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.CourseGradeSnapshots.cs");
        Assert.Contains("CapturePublishedObeComponentsAsync", capture);
        Assert.Contains("StudentResultCourseSnapshots AS grade", capture);
        Assert.Contains("StudentAssessmentComponentMarks AS detail", capture);
        Assert.Contains("actual != expected", capture);
        var publish = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.cs");
        var correction = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.ResultCorrections.cs");
        Assert.Contains("CapturePublishedObeComponentsAsync(publication, now, token)", publish);
        Assert.Contains("CapturePublishedObeComponentsAsync(replacement, now, token)", correction);
        var service = Read("EduOS.Service", "Services", "Research", "ObeAttainmentService.cs");
        Assert.Contains("ObePublishedComponentRegisters", service);
        Assert.Contains("ObePublishedComponentEvidence", service);
        Assert.Contains("legacyCourseOnly ? courseByClo", service);
        Assert.Contains("x.CourseOutcomeId,", service);
        Assert.DoesNotContain("db.StudentAssessmentComponentMarks.AsNoTracking()", service);
    }

    [Fact]
    public void Two_distinct_component_clos_in_one_course_keep_separate_evidence_and_precise_plo_weight()
    {
        // The published calculator keys rows by the immutable CLO ID, not by
        // curriculum course ID: two rubric components must remain distinguishable.
        var evidence = new[]
        {
            Row(1, 501, 90), Row(1, 502, 40),
            Row(2, 501, 70), Row(2, 502, 80)
        };
        var mapped = new[]
        {
            new PublishedOutcomeAttainmentPolicy.Mapping(600, 501, 25),
            new PublishedOutcomeAttainmentPolicy.Mapping(600, 502, 75)
        };
        var outcome = PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster(
            evidence, mapped, [1, 2]).Single();
        Assert.Equal(65m, outcome.AttainmentPercent);
        Assert.Throws<ArgumentException>(() => PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster(
            evidence[..3], mapped, [1, 2]));
    }

    [Fact]
    public void Legacy_course_proxy_create_request_digest_excludes_new_optional_null_component_code()
    {
        var row = new ObeCourseOutcomeInputDto
        { CurriculumSubjectId = 7, Code = "CLO1", Statement = "Read academic literature" };
        Assert.Equal(
            "{\"CurriculumSubjectId\":7,\"Code\":\"CLO1\",\"Statement\":\"Read academic literature\"}",
            JsonSerializer.Serialize(row));
    }

    [Fact]
    public void Service_uses_only_published_canonical_result_snapshots_with_lifecycle_and_tenant_checks()
    {
        var service = Read("EduOS.Service", "Services", "Research", "ObeAttainmentService.cs");
        Assert.Contains("access.RolesAsync", service);
        Assert.Contains("access.CampusAsync", service);
        Assert.Contains("ProgramScopeAsync", service);
        Assert.Contains("db.StudentResultCourseSnapshots.AsNoTracking()", service);
        Assert.Contains("db.StudentResultSummaries.AsNoTracking()", service);
        Assert.Contains("p.State == ResultPublicationState.Published", service);
        Assert.Contains("p.HasCourseGradeSnapshots", service);
        Assert.Contains("PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster", service);
        Assert.Contains("summary.PublicationVersionNo == scope.VersionNo", service);
        Assert.Contains("enrollment.AcademicBatchId == scope.AcademicBatchId", service);
        Assert.Contains("subject.AcademicCurriculumId == enrollment.AcademicCurriculumId", service);
        Assert.Contains("row.SubmittedByUserId == access.UserId", service);
        Assert.Contains("currentEvidence.Data.EvidenceSha256 != frozen.EvidenceSha256", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.DoesNotContain("INSERT INTO dbo.StudentAssessmentMarks", service);
        Assert.DoesNotContain("INSERT INTO dbo.AccJournal", service);
    }

    [Fact]
    public void Published_grade_policy_produces_reproducible_unrounded_weighted_cohort_and_rejects_missing_grade()
    {
        var rows = new[]
        {
            Row(1, 10, 80), Row(1, 20, 50),
            Row(2, 10, 60), Row(2, 20, 90)
        };
        var mapping = new[]
        {
            new PublishedOutcomeAttainmentPolicy.Mapping(300, 10, 60),
            new PublishedOutcomeAttainmentPolicy.Mapping(300, 20, 40)
        };
        var result = PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster(rows, mapping, [1, 2]);
        Assert.Equal(70m, result.Single().AttainmentPercent);
        Assert.Throws<ArgumentException>(() => PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster(
            rows[..3], mapping, [1, 2]));
    }

    private static PublishedOutcomeAttainmentPolicy.Evidence Row(long learner, long course, decimal mark) =>
        new(101, 2, learner, course, 100, mark, false, false, true, true);

    private static string Read(params string[] segments)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
