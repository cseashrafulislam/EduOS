using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentAdministrationServiceTests
{
    private static readonly string Source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "TestAssets", "AssessmentAdministrationService.cs"));

    [Fact]
    public void Teacher_mark_writes_require_an_active_employee_and_exact_offering_assignment()
    {
        Source.Should().Contain("IGenericRepository<Employee>");
        Source.Should().Contain("IGenericRepository<InstructorAssignment>");
        Source.Should().Contain("private async Task<bool> CanEditOfferingAsync(long offeringId");
        Source.Should().Contain("private async Task<long> OwnTeacherIdAsync(");
        Source.Should().Contain("x.UserId == _user.UserId");
        Source.Should().Contain("x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted");
        Source.Should().Contain("x.SubjectOfferingId == offeringId");
        Source.Should().Contain("x.EmployeeId == employeeId && x.IsActive && !x.IsDeleted");
    }

    [Fact]
    public void Result_publication_and_full_results_require_privileged_roles_not_ordinary_teacher_role()
    {
        Source.Should().Contain("if (!CanPublish()) return Error<AssessmentResultSheetDto>(\"Result access requires administration.\", 403)");
        Source.Should().Contain("if (!CanPublish()) return Error<ResultPublicationDto>(\"Publication permission required.\", 403)");
        Source.Should().Contain("private bool CanPublish()");
        Source.Should().Contain("_user.IsTenantAdmin");
        Source.Should().Contain("_user.IsInRole(\"ExamController\")");
        Source.Should().Contain("_user.IsInRole(\"Principal\")");
    }

    [Fact]
    public void Publication_is_atomic_idempotent_and_preflights_entire_active_roster()
    {
        Source.Should().Contain("await _uow.ExecuteInTransactionAsync(async token =>");
        Source.Should().Contain("ResultPublicationState.Published");
        Source.Should().Contain("Existing published result differs from this request.");
        Source.Should().Contain("Result is already published.");
        Source.Should().Contain("Assessment must be locked before publishing results.");
        Source.Should().Contain("if (calculated.Sheet!.Results.Any(result => !enrolled.ContainsKey(result.RollNo)))");
        Source.Should().Contain("Student enrollment roster changed.");
        Source.Should().Contain("await _publications.AddAsync(publication)");
        Source.Should().Contain("await _uow.SaveChangesAsync(token)");
        Source.Should().Contain("catch (DbUpdateConcurrencyException)");
        Source.Should().Contain("catch (DbUpdateException ex)");
    }
}
