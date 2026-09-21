using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ExamWorkflowServiceTests
{
    private static readonly string Source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "TestAssets",
        "ExamWorkflowService.cs"));

    [Fact]
    public void Teacher_mark_workflows_require_an_active_employee_and_exact_subject_assignment()
    {
        Source.Should().Contain("IGenericRepository<Employee>");
        Source.Should().Contain("IGenericRepository<SubjectTeacher>");
        Source.Should().Contain("x.UserId == _currentUser.UserId && x.IsActive && x.IsTeacher");
        Source.Should().Contain("x.AcademicYearId == academicYearId");
        Source.Should().Contain("x.ClassId == classId");
        Source.Should().Contain("x.SectionId == sectionId");
        Source.Should().Contain("x.SubjectId == subjectId");
        Source.Should().Contain("x.TeacherId == employeeId.Value");
    }

    [Fact]
    public void Whole_section_results_require_class_teacher_ownership_for_teacher_accounts()
    {
        Source.Should().Contain("x.TeacherId == employeeId.Value && x.IsClassTeacher");
        Source.Should().Contain("You are not assigned as this section's class teacher.");
    }

    [Fact]
    public void Publication_is_atomic_idempotent_and_rejects_incomplete_sections()
    {
        Source.Should().Contain("await _unitOfWork.BeginTransactionAsync()");
        Source.Should().Contain("await _unitOfWork.CommitTransactionAsync()");
        Source.Should().Contain("await _unitOfWork.RollbackTransactionAsync()");
        Source.Should().Contain("Results were already published.");
        Source.Should().Contain("Generate complete results before publishing.");
        Source.Should().Contain("No active students were found for this section.");
        Source.Should().Contain("catch (DbUpdateException ex)");
    }
}
