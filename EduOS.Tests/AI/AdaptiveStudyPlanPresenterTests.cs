using EduOS.AI;
using Xunit;
using static EduOS.AI.AdaptiveStudyPlanPolicy;

namespace EduOS.Tests.AI;

public sealed class AdaptiveStudyPlanPresenterTests
{
    private static StudyPlanResult Ready() => new(PlanStatus.Ready,
        [new SkillRecommendation("fractions", StudyPriority.Review, 42.5m, 2, "review_chapter"),
         new SkillRecommendation("geometry", StudyPriority.Practice, 72m, 1, "practice_skill"),
         new SkillRecommendation("algebra", StudyPriority.Extend, 90m, 3, "extend_learning")]);

    [Theory]
    [InlineData("bn-BD", "পুনরায় পড়ুন")]
    [InlineData("en-BD", "Review")]
    public void Published_evidence_is_localized_without_changing_marks(string locale, string expected)
    {
        var plan = Ready();
        var output = AdaptiveStudyPlanPresenter.Present(plan, locale);
        Assert.Equal(PlanStatus.Ready, output.Status);
        Assert.Equal(expected, output.Skills[0].Priority);
        Assert.Equal(42.5m, output.Skills[0].ObservedPercent);
        Assert.Equal(2, output.Skills[0].PublishedAttemptCount);
        Assert.Equal(72m, output.Skills[1].ObservedPercent);
        Assert.Equal(90m, output.Skills[2].ObservedPercent);
        Assert.Equal("review_chapter", plan.Skills[0].FeedbackCode);
    }

    [Theory]
    [InlineData(PlanStatus.Denied)]
    [InlineData(PlanStatus.NoEvidence)]
    [InlineData(PlanStatus.Invalid)]
    public void Nonready_results_never_expose_skill_data(PlanStatus status)
    {
        var malformed = new StudyPlanResult(status, Ready().Skills);
        var output = AdaptiveStudyPlanPresenter.Present(malformed, "bn-BD");
        Assert.Equal(status, output.Status);
        Assert.Empty(output.Skills);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsupported_locale_fails_closed(string? locale)
    {
        var output = AdaptiveStudyPlanPresenter.Present(Ready(), locale);
        Assert.Equal(PlanStatus.Invalid, output.Status);
        Assert.Empty(output.Skills);
    }

    [Fact]
    public void Malformed_skill_and_feedback_code_do_not_leak_partial_results()
    {
        var invalid = new StudyPlanResult(PlanStatus.Ready,
            [Ready().Skills[0], new SkillRecommendation("<script>", StudyPriority.Extend,
                90m, 1, "extend_learning")]);
        Assert.Equal(PlanStatus.Invalid,
            AdaptiveStudyPlanPresenter.Present(invalid, "en-BD").Status);
        Assert.Empty(AdaptiveStudyPlanPresenter.Present(invalid, "en-BD").Skills);

        var badFeedback = new StudyPlanResult(PlanStatus.Ready,
            [new SkillRecommendation("math", StudyPriority.Review, 30m, 1, "extend_learning")]);
        Assert.Equal(PlanStatus.Invalid,
            AdaptiveStudyPlanPresenter.Present(badFeedback, "bn-BD").Status);
    }

    [Fact]
    public void Missing_and_out_of_range_evidence_are_invalid()
    {
        Assert.Equal(PlanStatus.Invalid,
            AdaptiveStudyPlanPresenter.Present(null, "bn-BD").Status);
        var invalid = new StudyPlanResult(PlanStatus.Ready,
            [new SkillRecommendation("math", StudyPriority.Review, 101m, 1, "review_chapter")]);
        Assert.Equal(PlanStatus.Invalid,
            AdaptiveStudyPlanPresenter.Present(invalid, "en-BD").Status);
    }
}
