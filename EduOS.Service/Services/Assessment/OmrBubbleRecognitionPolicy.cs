namespace EduOS.Service.Services.Omr;

/// <summary>
/// Offline OMR bubble classifier for an already aligned/cropped sheet. Pixel
/// extraction and page registration are separate: measurements are untrusted
/// suggestions and must never become canonical assessment marks automatically.
/// </summary>
public static class OmrBubbleRecognitionPolicy
{
    public const int MaxQuestions = 200;
    public const int MaxPixelsPerBubble = 1_000_000;

    public static OmrRecognitionResult Evaluate(int questionCount, int optionsPerQuestion,
        IReadOnlyCollection<OmrBubbleSample> samples, OmrRecognitionCalibration? calibration = null)
    {
        if (questionCount is < 1 or > MaxQuestions) throw new ArgumentOutOfRangeException(nameof(questionCount));
        if (optionsPerQuestion is < 4 or > 5) throw new ArgumentOutOfRangeException(nameof(optionsPerQuestion));
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count != questionCount * optionsPerQuestion)
            throw new ArgumentException("Exactly one bubble measurement per question and option is required.", nameof(samples));

        calibration ??= new OmrRecognitionCalibration();
        if (calibration.BlankCeiling is < 0m or > 1m ||
            calibration.MarkedThreshold is < 0m or > 1m ||
            calibration.MinDarknessGap is <= 0m or > 1m ||
            calibration.BlankCeiling >= calibration.MarkedThreshold ||
            calibration.MarkedThreshold + calibration.MinDarknessGap > 1m)
            throw new ArgumentException("Invalid or uncalibrated OMR thresholds.", nameof(calibration));

        var grid = new decimal[questionCount, optionsPerQuestion];
        var encountered = new bool[questionCount, optionsPerQuestion];
        foreach (var sample in samples)
        {
            if (sample.QuestionNo < 1 || sample.QuestionNo > questionCount ||
                sample.OptionCode < 'A' || sample.OptionCode >= 'A' + optionsPerQuestion ||
                sample.SampledPixels < 1 || sample.SampledPixels > MaxPixelsPerBubble ||
                sample.DarkPixels < 0 || sample.DarkPixels > sample.SampledPixels)
                throw new ArgumentException("Invalid OMR pixel measurement or option.", nameof(samples));
            var q = sample.QuestionNo - 1;
            var o = sample.OptionCode - 'A';
            if (encountered[q, o])
                throw new ArgumentException("A question and option may be measured only once.", nameof(samples));
            encountered[q, o] = true;
            grid[q, o] = (decimal)sample.DarkPixels / sample.SampledPixels;
        }

        var results = new List<OmrQuestionSuggestion>(questionCount);
        for (var q = 0; q < questionCount; q++)
        {
            decimal highest = -1m, runnerUp = -1m;
            var winner = -1;
            for (var o = 0; o < optionsPerQuestion; o++)
            {
                if (!encountered[q, o])
                    throw new ArgumentException("OMR grid is incomplete.", nameof(samples));
                var darkness = grid[q, o];
                if (darkness > highest)
                {
                    runnerUp = highest;
                    highest = darkness;
                    winner = o;
                }
                else if (darkness > runnerUp) runnerUp = darkness;
            }

            var state = highest <= calibration.BlankCeiling ? OmrSuggestionState.Blank :
                highest < calibration.MarkedThreshold ? OmrSuggestionState.LowConfidence :
                runnerUp >= calibration.MarkedThreshold ||
                highest - runnerUp < calibration.MinDarknessGap ? OmrSuggestionState.Ambiguous :
                OmrSuggestionState.Single;
            results.Add(new OmrQuestionSuggestion
            {
                QuestionNo = q + 1,
                State = state,
                SuggestedOption = state == OmrSuggestionState.Single ? (char)('A' + winner) : null,
                HighestDarkness = decimal.Round(highest, 4, MidpointRounding.AwayFromZero),
                RunnerUpDarkness = decimal.Round(runnerUp, 4, MidpointRounding.AwayFromZero)
            });
        }

        return new OmrRecognitionResult
        {
            Questions = results,
            UncertainCount = results.Count(x => x.State != OmrSuggestionState.Single),
            RequiresIndependentReview = true
        };
    }
}

public sealed class OmrBubbleSample
{
    public int QuestionNo { get; init; }
    public char OptionCode { get; init; }
    public int DarkPixels { get; init; }
    public int SampledPixels { get; init; }
}

public sealed class OmrRecognitionCalibration
{
    public decimal BlankCeiling { get; init; } = 0.18m;
    public decimal MarkedThreshold { get; init; } = 0.45m;
    public decimal MinDarknessGap { get; init; } = 0.15m;
}

public enum OmrSuggestionState
{
    Blank = 1,
    Single = 2,
    Ambiguous = 3,
    LowConfidence = 4
}

public sealed class OmrQuestionSuggestion
{
    public int QuestionNo { get; init; }
    public OmrSuggestionState State { get; init; }
    public char? SuggestedOption { get; init; }
    public decimal HighestDarkness { get; init; }
    public decimal RunnerUpDarkness { get; init; }
}

public sealed class OmrRecognitionResult
{
    public IReadOnlyList<OmrQuestionSuggestion> Questions { get; init; } = [];
    public int UncertainCount { get; init; }
    public bool RequiresIndependentReview { get; init; }
}
