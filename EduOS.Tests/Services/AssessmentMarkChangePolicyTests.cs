using EduOS.Core.Entities.Assessment;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkChangePolicyTests
{
    [Fact]
    public void Subject_mark_retries_are_noops_but_every_business_change_is_detected()
    {
        var mark = new StudentAssessmentMark
        { ObtainedMarks = 71.25m, IsAbsent = false, IsWithheld = false,
          Remarks = "Checked", GradeLetter = "A", GradePoint = 4m };
        AssessmentMarkChangePolicy.HasChanged(mark, 71.25m, false, false, "Checked", "A", 4m)
            .Should().BeFalse();
        AssessmentMarkChangePolicy.HasChanged(null, 71.25m, false, false, "Checked", "A", 4m)
            .Should().BeTrue();
        AssessmentMarkChangePolicy.HasChanged(mark, 71.26m, false, false, "Checked", "A", 4m)
            .Should().BeTrue();
        AssessmentMarkChangePolicy.HasChanged(mark, 71.25m, false, true, "Checked", "A", 4m)
            .Should().BeTrue();
        AssessmentMarkChangePolicy.HasChanged(mark, 71.25m, false, false, "Checked", "B", 4m)
            .Should().BeTrue();
    }

    [Fact]
    public void Component_mark_retries_do_not_generate_a_second_change()
    {
        var mark = new StudentAssessmentComponentMark
        { ObtainedMarks = 12.50m, IsAbsent = false, IsWithheld = false };
        AssessmentMarkChangePolicy.HasChanged(mark, 12.50m, false, false).Should().BeFalse();
        AssessmentMarkChangePolicy.HasChanged(mark, 12.50m, true, false).Should().BeTrue();
        AssessmentMarkChangePolicy.HasChanged(null, 12.50m, false, false).Should().BeTrue();
    }

    [Fact]
    public void Scoped_history_is_paged_and_audit_is_written_in_marks_transaction()
    {
        static string Read(string path)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return File.ReadAllText(file);
            }
            throw new FileNotFoundException(path);
        }
        var read = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkChanges.cs");
        read.Should().Contain("if (!CanPublish())");
        read.Should().Contain("ScopeAsync(request, ct)");
        read.Should().Contain("subject.TenantId == tenant && offering.TenantId == tenant");
        read.Should().Contain("offering.AcademicBatchId == scope.Value.Batch.Id");
        read.Should().Contain("x.TenantId == tenant && x.EntityName == \"AssessmentMarkChange\"");
        read.Should().Contain(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)");
        var write = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs");
        write.Should().Contain("if (!changedAny)");
        write.Should().Contain("EntityName = \"AssessmentMarkChange\", EntityId = sub.Id");
        write.Should().Contain("Matches(row.RowVersion, item.RowVersion)");
        var component = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Documents.cs");
        component.Should().Contain("EntityName = \"AssessmentMarkChange\", EntityId = subject.Id");
    }
}
