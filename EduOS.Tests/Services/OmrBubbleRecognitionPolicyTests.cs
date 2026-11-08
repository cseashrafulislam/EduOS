using EduOS.Service.Services.Assessment;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class OmrBubbleRecognitionPolicyTests
{
    private static List<OmrBubbleSample> Grid(params int[][] questionDarkPixels)
    {
        return questionDarkPixels.SelectMany((values, q) => values.Select((pixels, option) =>
            new OmrBubbleSample { QuestionNo = q + 1, OptionCode = (char)('A' + option),
                DarkPixels = pixels, SampledPixels = 100 })).ToList();
    }

    [Fact]
    public void Clear_marks_are_only_suggestions_never_auto_approved()
    {
        var result = OmrBubbleRecognitionPolicy.Evaluate(2, 4,
            Grid([7, 83, 8, 9], [65, 8, 7, 10]));
        Assert.Equal('B', result.Questions[0].SuggestedOption);
        Assert.Equal('A', result.Questions[1].SuggestedOption);
        Assert.All(result.Questions, x => Assert.Equal(OmrSuggestionState.Single, x.State));
        Assert.Equal(0, result.UncertainCount);
        Assert.True(result.RequiresIndependentReview);
    }

    [Fact]
    public void Blank_uncertain_and_multiple_marks_fail_closed()
    {
        var result = OmrBubbleRecognitionPolicy.Evaluate(4, 4,
            Grid([9, 10, 8, 9], [22, 36, 12, 5], [5, 78, 73, 6], [35, 49, 9, 6]));
        Assert.Equal(new[] { OmrSuggestionState.Blank, OmrSuggestionState.LowConfidence,
            OmrSuggestionState.Ambiguous, OmrSuggestionState.Ambiguous },
            result.Questions.Select(x => x.State));
        Assert.All(result.Questions, x => Assert.Null(x.SuggestedOption));
        Assert.Equal(4, result.UncertainCount);
    }

    [Fact]
    public void Custom_calibration_is_explicit_and_bound()
    {
        var measurement = Grid([7, 52, 32, 10]);
        Assert.Equal(OmrSuggestionState.Single,
            OmrBubbleRecognitionPolicy.Evaluate(1, 4, measurement).Questions[0].State);
        Assert.Equal(OmrSuggestionState.Ambiguous,
            OmrBubbleRecognitionPolicy.Evaluate(1, 4, measurement,
                new OmrRecognitionCalibration { BlankCeiling = 0.1m,
                    MarkedThreshold = 0.4m, MinDarknessGap = 0.25m }).Questions[0].State);
        Assert.Throws<ArgumentException>(() => OmrBubbleRecognitionPolicy.Evaluate(1, 4,
            measurement, new OmrRecognitionCalibration { MarkedThreshold = 0.1m }));
    }

    [Fact]
    public void Incomplete_duplicate_invalid_and_excessive_grids_are_rejected()
    {
        var valid = Grid([10, 80, 8, 9]);
        Assert.Throws<ArgumentException>(() => OmrBubbleRecognitionPolicy.Evaluate(1, 4, valid.Take(3).ToList()));
        var duplicate = valid.ToList();
        duplicate[1] = duplicate[0];
        Assert.Throws<ArgumentException>(() => OmrBubbleRecognitionPolicy.Evaluate(1, 4, duplicate));
        var negative = valid.ToList();
        negative[0] = new OmrBubbleSample { QuestionNo = 1, OptionCode = 'A',
            DarkPixels = -1, SampledPixels = 100 };
        Assert.Throws<ArgumentException>(() => OmrBubbleRecognitionPolicy.Evaluate(1, 4, negative));
        Assert.Throws<ArgumentOutOfRangeException>(() => OmrBubbleRecognitionPolicy.Evaluate(201, 4, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => OmrBubbleRecognitionPolicy.Evaluate(1, 6, []));
    }

    [Fact]
    public void Five_option_answer_sheets_preserve_option_order()
    {
        var result = OmrBubbleRecognitionPolicy.Evaluate(1, 5, Grid([2, 2, 3, 5, 90]));
        Assert.Equal('E', result.Questions[0].SuggestedOption);
    }
}
