using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkReviewWorkflowContractTests
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
    public void Review_requires_tenant_campus_assignment_separate_reviewer_and_rowversion()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviews.cs");
        service.Should().Contain("x.TenantId == tenant && x.Id == subjectId");
        service.Should().Contain("CanAccessCampusAsync(exam.CampusId");
        service.Should().Contain("CanSubmitReviewAsync(subject.SubjectOfferingId");
        service.Should().Contain("x.EmployeeId == employeeId && x.IsActive && !x.IsDeleted");
        service.Should().Contain("latest.ActorUserId != _user.UserId");
        service.Should().Contain("latest.ActorUserId == _user.UserId");
        service.Should().Contain("Matches(exam.RowVersion, request.AssessmentRowVersion)");
        service.Should().Contain("latest.MarkRowVersion != Version(subject.RowVersion)");
        service.Should().Contain("ClientRequestId == request.ClientRequestId");
        service.Should().Contain("CountAsync(token)");
        service.Should().Contain("Take(50)");
    }

    [Fact]
    public void Teacher_submission_checks_component_values_and_orphaned_evidence_in_sql()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviews.cs");
        service.Should().Contain("var allDetails = from part in _componentMarks");
        service.Should().Contain("await allDetails.LongCountAsync(token)");
        service.Should().Contain("var reconciled = from item in joined");
        service.Should().Contain("group item by new");
        service.Should().Contain("Math.Round(x.WeightedMarks * subject.FullMarks / 100m, 2)");
        service.Should().Contain("x.AbsentCount == components.Count");
        service.Should().Contain("Component totals, absence flags or subject marks require reconciliation.");
        service.Should().Contain("Unexpected component marks require reconciliation.");
        service.Should().Contain("(!mark.IsAbsent || mark.ObtainedMarks == 0m)");
    }

    [Fact]
    public void Mark_writes_and_lock_enforce_review_evidence()
    {
        var marks = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        var parts = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Documents.cs");
        var setup = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs");
        marks.Should().Contain("Review decision prevents marks editing.");
        parts.Should().Contain("Review decision prevents component editing.");
        marks.Should().Contain("_subjects.Update(sub);");
        parts.Should().Contain("_subjects.Update(subject);");
        setup.Should().Contain("All assessment subjects require approved, current mark review before locking.");
    }

    [Fact]
    public void Finalized_result_history_freezes_mark_component_review_and_scheme_writes()
    {
        var freeze = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.PublicationFreeze.cs");
        var marks = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        var parts = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Documents.cs");
        var setup = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs");
        var reviews = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviews.cs");
        var context = Read("EduOS.Persistence/Context/EduOSDbContext.cs");
        freeze.Should().Contain("x.TenantId == _user.TenantId");
        freeze.Should().Contain("ResultPublicationState.Published");
        freeze.Should().Contain("ResultPublicationState.Withdrawn");
        marks.Should().Contain("HasFinalizedPublicationAsync(exam.Id, offering.AcademicBatchId");
        parts.Should().Contain("HasFinalizedPublicationAsync(exam.Id, batch.Id");
        setup.Should().Contain("HasFinalizedPublicationAsync(exam.Id, batch.Id");
        reviews.Should().Contain("HasFinalizedPublicationAsync(exam.Id, batch.Id");
        reviews.Should().Contain("CanReview = !finalized");
        context.Should().Contain("if (entry.Entity is StudentResultSummary)");
        context.Should().Contain("OriginalStateEquals(entry, nameof(ResultPublication.State), \"Withdrawn\")");
    }

    [Fact]
    public void Review_history_is_append_only_and_has_tenant_scoped_database_keys()
    {
        var context = Read("EduOS.Persistence/Context/EduOSDbContext.cs");
        var migration = Read("EduOS.Persistence/Migrations/20261028042000_AssessmentMarkReviewEvents.cs");
        context.Should().Contain("or AssessmentMarkReviewEvent");
        migration.Should().Contain("AK_AssessmentMarkReviewEvents_TenantId_Id");
        migration.Should().Contain("IX_AssessmentMarkReviewEvents_TenantId_ClientRequestId");
        migration.Should().Contain("FK_AssessmentMarkReviewEvents_AssessmentSubjects_TenantId_AssessmentSubjectId");
    }
}
