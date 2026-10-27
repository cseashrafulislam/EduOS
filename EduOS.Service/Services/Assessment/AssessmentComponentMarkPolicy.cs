using EduOS.Core.Entities.Assessment;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Deterministic component-to-subject reconciliation; never mutates published evidence.</summary>
public static class AssessmentComponentMarkPolicy
{
    public sealed record Result(string Status, decimal? ExpectedSubjectMarks,
        bool IsReconciled, bool MandatoryComponentsPassed);

    public static Result Evaluate(decimal subjectFullMarks, decimal subjectMarks,
        bool subjectAbsent, bool subjectWithheld,
        IReadOnlyList<AssessmentComponent> components,
        IReadOnlyList<StudentAssessmentComponentMark> marks)
    {
        if (components.Count == 0)
            return marks.Count == 0 ? new("NotConfigured", null, true, true)
                : new("UnexpectedComponentMark", null, false, false);
        if (components.Count > 50 || subjectFullMarks <= 0 ||
            components.Select(x => x.Id).Distinct().Count() != components.Count ||
            components.Any(x => x.FullMarks <= 0 || x.PassMarks < 0 ||
                x.PassMarks > x.FullMarks || x.Weightage <= 0 || x.Weightage > 100) ||
            components.Sum(x => x.Weightage) != 100m)
            return new("InvalidComponentScheme", null, false, false);
        var definitions = components.ToDictionary(x => x.Id);
        if (marks.Any(x => !definitions.ContainsKey(x.AssessmentComponentId)))
            return new("UnexpectedComponentMark", null, false, false);
        if (marks.GroupBy(x => x.AssessmentComponentId).Any(x => x.Count() != 1))
            return new("DuplicateComponentMark", null, false, false);
        if (marks.Count != components.Count)
            return new("MissingComponentMark", null, false, false);
        var entries = marks.ToDictionary(x => x.AssessmentComponentId);
        if (components.Any(x =>
        {
            var mark = entries[x.Id];
            return mark.ObtainedMarks < 0 || mark.ObtainedMarks > x.FullMarks ||
                mark.IsAbsent && mark.IsWithheld ||
                (mark.IsAbsent || mark.IsWithheld) && mark.ObtainedMarks != 0 ||
                decimal.Round(mark.ObtainedMarks, 2) != mark.ObtainedMarks;
        }))
            return new("InvalidComponentMark", null, false, false);
        var expected = decimal.Round(
            components.Sum(x => entries[x.Id].ObtainedMarks / x.FullMarks * x.Weightage)
                * subjectFullMarks / 100m, 2, MidpointRounding.AwayFromZero);
        if (subjectMarks != expected)
            return new("SubjectTotalMismatch", expected, false, false);
        if (subjectAbsent != marks.All(x => x.IsAbsent) ||
            subjectWithheld != marks.Any(x => x.IsWithheld))
            return new("SubjectFlagMismatch", expected, false, false);
        var mandatoryPassed = components.All(x => !x.IsMandatoryPass ||
            (!entries[x.Id].IsAbsent && !entries[x.Id].IsWithheld &&
                entries[x.Id].ObtainedMarks >= x.PassMarks));
        return new(mandatoryPassed ? "Reconciled" : "MandatoryComponentFailed",
            expected, true, mandatoryPassed);
    }
}
