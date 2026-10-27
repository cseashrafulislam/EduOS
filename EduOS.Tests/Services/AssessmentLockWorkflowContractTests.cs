using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentLockWorkflowContractTests
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
    public void Lock_reconciles_every_tenant_owned_batch_before_advancing_state()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs");
        source.Should().Contain("request.State == AssessmentState.Locked");
        source.Should().Contain("x.TenantId == _user.TenantId && x.AssessmentId == row.Id");
        source.Should().Contain("Duplicate assessment subject offerings require reconciliation.");
        source.Should().Contain("x.CampusId == row.CampusId");
        source.Should().Contain("x.AcademicYearId == row.AcademicYearId");
        source.Should().Contain("foreach (var batch in batches)");
        source.Should().Contain("await CalculateAsync(row, batch, token)");
        source.Should().Contain("sheet.Results.Count == 0");
        source.Should().Contain("Assessment spans more than 20 batches");
        source.Should().Contain("_assessments.Update(row); await _uow.SaveChangesAsync(token);");
    }

    [Fact]
    public void Subject_and_component_writers_conflict_with_competing_lock()
    {
        var marks = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        var components = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Documents.cs");
        marks.Should().Contain("exam.UpdatedAt = now; exam.UpdatedBy = _user.UserId;");
        marks.Should().Contain("_assessments.Update(exam);");
        components.Should().Contain("exam.UpdatedAt = now; exam.UpdatedBy = _user.UserId;");
        components.Should().Contain("_assessments.Update(exam);");
        marks.Should().Contain("catch (DbUpdateConcurrencyException)");
        components.Should().Contain("WriteAsync(\"assessment component marks\"");
    }
}
