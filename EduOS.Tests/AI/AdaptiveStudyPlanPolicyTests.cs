using EduOS.AI;
using Xunit;
using static EduOS.AI.AdaptiveStudyPlanPolicy;
using PlanStatus = EduOS.AI.AdaptiveStudyPlanPolicy.PlanStatus;

namespace EduOS.Tests.AI;

public sealed class AdaptiveStudyPlanPolicyTests
{
    private static readonly StudyPlanRequest Request =
        new("tenant-1", "campus-1", "learner-1", "chapter-1");

    private static PublishedAttemptEvidence Row(
        string skill = "skill-a", decimal earned = 4m, decimal possible = 10m,
        string tenant = "tenant-1", string campus = "campus-1",
        string learner = "learner-1", bool published = true, bool visible = true) =>
        new(tenant, campus, learner, "assessment-1", "chapter-1", skill,
            earned, possible, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            visible, published);

    [Fact]
    public void Published_canonical_evidence_produces_ranked_read_only_recommendations()
    {
        var rows = new[] { Row("skill-high", 9), Row("skill-low", 2),
            Row("skill-mid", 6) };
        var result = Build(Request, rows);
        Assert.Equal(PlanStatus.Ready, result.Status);
        Assert.Equal(new[] { "skill-low", "skill-mid", "skill-high" },
            result.Skills.Select(x => x.SkillId));
        Assert.Equal(new[] { StudyPriority.Review, StudyPriority.Practice,
            StudyPriority.Extend }, result.Skills.Select(x => x.Priority));
        Assert.Equal(3, rows.Length); // No writes or mutation of source evidence.
    }

    [Theory]
    [InlineData("tenant-other", "campus-1", "learner-1")]
    [InlineData("tenant-1", "campus-other", "learner-1")]
    [InlineData("tenant-1", "campus-1", "learner-other")]
    public void Cross_scope_rows_fail_closed(string tenant, string campus, string learner)
    {
        var result = Build(Request, [Row(), Row(tenant: tenant, campus: campus,
            learner: learner)]);
        Assert.Equal(PlanStatus.Denied, result.Status);
        Assert.Empty(result.Skills);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Unpublished_or_hidden_assessments_are_denied(bool published, bool visible)
    {
        Assert.Equal(PlanStatus.Denied,
            Build(Request, [Row(published: published, visible: visible)]).Status);
    }

    [Fact]
    public void Missing_evidence_is_not_low_proficiency()
    {
        Assert.Equal(PlanStatus.NoEvidence, Build(Request, []).Status);
        Assert.Equal(PlanStatus.NoEvidence, Build(Request, null).Status);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    [InlineData(0, 0)]
    public void Invalid_marks_are_rejected(decimal earned, decimal possible)
    {
        Assert.Equal(PlanStatus.Invalid,
            Build(Request, [Row(earned: earned, possible: possible)]).Status);
    }

    [Fact]
    public void Multiple_attempts_use_published_weighted_marks_without_changing_grades()
    {
        var rows = new[] { Row(earned: 3, possible: 10),
            Row(earned: 18, possible: 20) };
        var result = Build(Request, rows);
        Assert.Equal(PlanStatus.Ready, result.Status);
        Assert.Equal(70m, result.Skills.Single().ObservedPercent);
        Assert.Equal(2, result.Skills.Single().PublishedAttemptCount);
        Assert.Equal(3m, rows[0].EarnedMarks);
        Assert.Equal(18m, rows[1].EarnedMarks);
    }

    [Fact]
    public void Limit_and_invalid_scope_fail_closed()
    {
        Assert.Equal(PlanStatus.Invalid, Build(Request with { MaxSkills = 0 },
            [Row()]).Status);
        Assert.Equal(PlanStatus.Denied, Build(Request with { LearnerId = "" },
            [Row()]).Status);
    }

    [Fact]
    public void A_different_chapter_is_denied_even_with_same_learner()
    {
        Assert.Equal(PlanStatus.Denied, Build(Request with { ChapterId = "chapter-2" },
            [Row()]).Status);
    }
}
