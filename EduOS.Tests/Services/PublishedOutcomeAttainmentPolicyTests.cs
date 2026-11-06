using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Xunit;
using Policy = EduOS.Service.Services.AssessmentAdministration.PublishedOutcomeAttainmentPolicy;

namespace EduOS.Tests.Services;

public sealed class PublishedOutcomeAttainmentPolicyTests
{
    private static Policy.Evidence Row(long enrollment, long course, decimal obtained,
        decimal full = 100m) => new(91, 3, enrollment, course, full, obtained,
            false, false, true, true);

    private static Policy.Mapping Map(long outcome, long course, decimal weight) =>
        new(outcome, course, weight);

    [Fact]
    public void Published_course_means_are_weighted_once_per_mapping_and_round_deterministically()
    {
        var result = Policy.Calculate(
            [Row(1, 10, 70), Row(2, 10, 80), Row(1, 20, 90), Row(2, 20, 60)],
            [Map(5, 10, 60), Map(5, 20, 40)]);
        result.Should().ContainSingle().Which.Should().Be(
            new Policy.Outcome(5, 75m, 4, 91, 3));
    }

    [Fact]
    public void Nonuniform_course_class_size_does_not_reweight_policy()
    {
        var result = Policy.Calculate(
            [Row(1, 10, 100), Row(2, 10, 100), Row(3, 10, 100), Row(1, 20, 0)],
            [Map(1, 10, 50), Map(1, 20, 50)]);
        result.Single().AttainmentPercent.Should().Be(50m);
    }

    [Fact]
    public void Decimal_rounding_uses_away_from_zero_at_requested_precision()
    {
        var result = Policy.Calculate([Row(1, 10, 1, 16)],
            [Map(1, 10, 100)], 2);
        result.Single().AttainmentPercent.Should().Be(6.25m);
        Action invalid = () => Policy.Calculate([Row(1, 10, 50)],
            [Map(1, 10, 100)], 7);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void Unpublished_unreconciled_absent_or_withheld_evidence_fails_closed(
        bool published, bool reconciled, bool absent, bool withheld)
    {
        var row = Row(1, 10, 80) with { IsPublished = published,
            IsReconciled = reconciled, IsAbsent = absent, IsWithheld = withheld };
        Action act = () => Policy.Calculate([row], [Map(1, 10, 100)]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mixed_versions_duplicates_and_invalid_marks_are_rejected()
    {
        var row = Row(1, 10, 80);
        Action mixed = () => Policy.Calculate([row, Row(2, 10, 70) with
            { PublicationVersion = 4 }], [Map(1, 10, 100)]);
        Action duplicate = () => Policy.Calculate([row, row],
            [Map(1, 10, 100)]);
        Action invalid = () => Policy.Calculate([row with { ObtainedMarks = 101 }],
            [Map(1, 10, 100)]);
        mixed.Should().Throw<ArgumentException>();
        duplicate.Should().Throw<ArgumentException>();
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_weights_duplicate_mappings_and_missing_course_evidence_fail_closed()
    {
        var rows = new[] { Row(1, 10, 80), Row(1, 20, 60) };
        Action weight = () => Policy.Calculate(rows,
            [Map(1, 10, 60), Map(1, 20, 30)]);
        Action duplicate = () => Policy.Calculate(rows,
            [Map(1, 10, 50), Map(1, 10, 50)]);
        Action missing = () => Policy.Calculate(rows, [Map(1, 30, 100)]);
        Action negative = () => Policy.Calculate(rows, [Map(1, 10, -1)]);
        weight.Should().Throw<ArgumentException>();
        duplicate.Should().Throw<ArgumentException>();
        missing.Should().Throw<ArgumentException>();
        negative.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Multiple_outcomes_are_ordered_and_calculated_independently()
    {
        var result = Policy.Calculate([Row(1, 10, 80), Row(1, 20, 60)],
            [Map(20, 20, 100), Map(10, 10, 100)]);
        result.Select(x => x.OutcomeId).Should().Equal(10, 20);
        result.Select(x => x.AttainmentPercent).Should().Equal(80m, 60m);
    }

    [Fact]
    public void Publication_identity_and_course_evidence_must_be_positive()
    {
        var baseline = Row(1, 10, 80);
        foreach (var invalid in new[]
        {
            baseline with { PublicationId = 0 },
            baseline with { PublicationVersion = 0 },
            baseline with { EnrollmentId = 0 },
            baseline with { CurriculumSubjectId = 0 },
            baseline with { FullMarks = 0 },
            baseline with { ObtainedMarks = -1 }
        })
        {
            Action act = () => Policy.Calculate([invalid], [Map(1, 10, 100)]);
            act.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void Same_version_from_different_publications_cannot_be_combined()
    {
        Action act = () => Policy.Calculate(
            [Row(1, 10, 70), Row(2, 10, 80) with { PublicationId = 92 }],
            [Map(1, 10, 100)]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Fractional_weights_are_supported_without_premature_rounding()
    {
        var result = Policy.Calculate(
            [Row(1, 10, 100), Row(1, 20, 0)],
            [Map(1, 10, 33.333m), Map(1, 20, 66.667m)], 3);
        result.Single().AttainmentPercent.Should().Be(33.333m);
    }

    [Fact]
    public void Input_collections_are_not_modified_by_calculation()
    {
        var rows = new List<Policy.Evidence> { Row(1, 20, 60), Row(1, 10, 80) };
        var maps = new List<Policy.Mapping> { Map(2, 20, 100), Map(1, 10, 100) };
        var originalRows = rows.ToArray();
        var originalMaps = maps.ToArray();
        Policy.Calculate(rows, maps);
        rows.Should().Equal(originalRows);
        maps.Should().Equal(originalMaps);
    }
}
