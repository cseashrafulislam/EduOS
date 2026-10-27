using EduOS.Core.Entities.Assessment;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentComponentReconciliationTests
{
    private static AssessmentComponent[] Scheme() =>
    [
        new() { Id = 11, FullMarks = 50m, PassMarks = 20m,
            Weightage = 60m, IsMandatoryPass = true },
        new() { Id = 12, FullMarks = 100m, PassMarks = 40m,
            Weightage = 40m, IsMandatoryPass = false }
    ];

    private static StudentAssessmentComponentMark[] Entries(decimal written = 40m,
        decimal practical = 75m) =>
    [
        new() { AssessmentComponentId = 11, ObtainedMarks = written },
        new() { AssessmentComponentId = 12, ObtainedMarks = practical }
    ];

    [Fact]
    public void Weighted_component_total_matches_canonical_subject_marks()
    {
        var result = AssessmentComponentMarkPolicy.Evaluate(100m, 78m, false, false,
            Scheme(), Entries());
        result.IsReconciled.Should().BeTrue();
        result.Status.Should().Be("Reconciled");
        result.ExpectedSubjectMarks.Should().Be(78m);
        result.MandatoryComponentsPassed.Should().BeTrue();
    }

    [Fact]
    public void Weighted_total_scales_to_subject_full_marks_and_rejects_mismatch()
    {
        var valid = AssessmentComponentMarkPolicy.Evaluate(80m, 62.40m, false, false,
            Scheme(), Entries());
        valid.IsReconciled.Should().BeTrue();
        valid.ExpectedSubjectMarks.Should().Be(62.40m);
        var invalid = AssessmentComponentMarkPolicy.Evaluate(100m, 80m, false, false,
            Scheme(), Entries());
        invalid.Status.Should().Be("SubjectTotalMismatch");
        invalid.IsReconciled.Should().BeFalse();
    }

    [Fact]
    public void Incomplete_duplicate_or_unexpected_component_evidence_blocks_publication()
    {
        var scheme = Scheme();
        AssessmentComponentMarkPolicy.Evaluate(100m, 48m, false, false,
            scheme, Entries()[..1]).Status.Should().Be("MissingComponentMark");
        AssessmentComponentMarkPolicy.Evaluate(100m, 78m, false, false,
            scheme, [Entries()[0], Entries()[0], Entries()[1]])
            .Status.Should().Be("DuplicateComponentMark");
        AssessmentComponentMarkPolicy.Evaluate(100m, 78m, false, false,
            scheme, [Entries()[0], new() { AssessmentComponentId = 999, ObtainedMarks = 75m }])
            .Status.Should().Be("UnexpectedComponentMark");
        AssessmentComponentMarkPolicy.Evaluate(100m, 0m, false, false,
            [], [Entries()[0]]).Status.Should().Be("UnexpectedComponentMark");
    }

    [Fact]
    public void Invalid_weights_and_invalid_absence_flags_block_publication()
    {
        var scheme = Scheme();
        scheme[1].Weightage = 30m;
        AssessmentComponentMarkPolicy.Evaluate(100m, 78m, false, false,
            scheme, Entries()).Status.Should().Be("InvalidComponentScheme");
        var invalid = Entries();
        invalid[0].IsAbsent = true;
        AssessmentComponentMarkPolicy.Evaluate(100m, 78m, false, false,
            Scheme(), invalid).Status.Should().Be("InvalidComponentMark");
    }

    [Fact]
    public void Withheld_and_absent_flags_must_match_subject_master()
    {
        var marks = Entries(0m, 0m);
        marks[0].IsAbsent = true; marks[1].IsAbsent = true;
        AssessmentComponentMarkPolicy.Evaluate(100m, 0m, false, false,
            Scheme(), marks).Status.Should().Be("SubjectFlagMismatch");
        AssessmentComponentMarkPolicy.Evaluate(100m, 0m, true, false,
            Scheme(), marks).IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void Mandatory_component_failure_is_a_publishable_fail_not_a_missing_mark()
    {
        var result = AssessmentComponentMarkPolicy.Evaluate(100m, 30m, false, false,
            Scheme(), Entries(0m, 75m));
        result.Status.Should().Be("MandatoryComponentFailed");
        result.IsReconciled.Should().BeTrue();
        result.MandatoryComponentsPassed.Should().BeFalse();
    }

    [Fact]
    public void Result_calculation_and_readiness_use_same_component_policy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        string Read(string name)
        {
            for (var dir = root; dir != null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "EduOS.Service", "Services", "Assessment", name);
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            throw new FileNotFoundException(name);
        }
        Read("AssessmentAdministrationService.cs")
            .Should().Contain("Component marks require reconciliation before publication.");
        Read("AssessmentAdministrationService.Readiness.cs")
            .Should().Contain("if (!componentCheck.IsReconciled) Issue(componentCheck.Status)");
        Read("AssessmentAdministrationService.ComponentMarks.cs")
            .Should().Contain("ReconciliationStatus = check?.Status");
    }
}
