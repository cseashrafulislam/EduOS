using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using EduOS.Service.Services.Exams;
using FluentAssertions;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class SubjectCompletionWorkflowTests
{
    private static readonly DateOnly End = new(2025, 12, 31);
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly SubjectCompletionEvidence Good = new(true, true, false, true, true, true);
    [Fact]
    public void Year_end_publication_pass_and_component_evidence_are_required()
    {
        SubjectCompletionPolicy.BlockReason(End, End, [Good]).Should().Contain("Academic year");
        SubjectCompletionPolicy.BlockReason(End, Today, []).Should().Contain("No assessments");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { Published = false }]).Should().Contain("published");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { SummaryPresent = false }]).Should().Contain("snapshot");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { Withheld = true }]).Should().Contain("Withheld");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { MarkPresent = false }]).Should().Contain("marks missing");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { Passed = false }]).Should().Contain("did not pass");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good with { ComponentsComplete = false }]).Should().Contain("Component");
        SubjectCompletionPolicy.BlockReason(End, Today, [Good, Good]).Should().BeNull();
    }
    [Fact]
    public void Query_rejects_invalid_scope_and_paging()
    {
        var invalid = new SubjectCompletionQueryDto { AcademicBatchId = 0, PageSize = 51 };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(invalid, new ValidationContext(invalid), errors, true).Should().BeFalse();
        var valid = new SubjectCompletionQueryDto { AcademicBatchId = 1 };
        errors.Clear();
        Validator.TryValidateObject(valid, new ValidationContext(valid), errors, true).Should().BeTrue();
    }
    private static string Read(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
    [Fact]
    public void Write_is_tenant_scoped_audited_idempotent_and_concurrency_checked()
    {
        var service = Read("EduOS.Service","Services","Exams","ExamWorkflowService.SubjectCompletions.cs");
        service.Should().Contain("registration.TenantId == tenant");
        service.Should().Contain("ResultPublicationState.Published");
        service.Should().Contain("SubjectCompletionPolicy.BlockReason");
        service.Should().Contain("Matches(row.RowVersion, request.RowVersion)");
        service.Should().Contain("Subject already completed.");
        service.Should().Contain("RecordAuditAsync");
        service.Should().Contain("GetSubjectCompletionBatchesAsync");
        service.Should().Contain("year.EndDate < today");
        Read("EduOS.Service","Services","Exams","ExamWorkflowService.Documents.cs")
            .Should().Contain("Completed subject registrations prevent publication withdrawal.");
    }
    [Fact]
    public void End_to_end_api_view_js_and_menu_are_wired()
    {
        Read("EduOS.App","Controllers","Api","ExamWorkflowController.cs")
            .Should().Contain("[HttpGet(\"subject-completions\")]")
            .And.Contain("[HttpPost(\"subject-completions/{id:long}/complete\")]");
        Read("EduOS.App","Controllers","ExamOperationsController.cs")
            .Should().Contain("public IActionResult SubjectCompletions() => View()");
        Read("EduOS.App","Views","Shared","_Layout.cshtml")
            .Should().Contain("href=\"/ExamOperations/SubjectCompletions\"");
        Read("EduOS.App","Views","ExamOperations","SubjectCompletions.cshtml")
            .Should().Contain("@Html.AntiForgeryToken()");
        var js = Read("EduOS.App","wwwroot","js","exams","subject-completions.js");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("/api/exams/workflow/subject-completions/batches");
        js.Should().Contain("td.textContent");
        js.Should().Contain("rowVersion: row.rowVersion");
        js.Should().Contain("URL.createObjectURL");
    }
}
