namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>
/// Pure, read-only OBE calculation over already authorized and reconciled published
/// course-grade evidence. The caller must resolve tenant/campus/record permissions
/// and publication version before constructing these inputs.
/// This does not create or change marks, grades, or approved OBE snapshots.
/// </summary>
public static class PublishedOutcomeAttainmentPolicy
{
    public sealed record Evidence(long PublicationId, int PublicationVersion,
        long EnrollmentId, long CurriculumSubjectId, decimal FullMarks,
        decimal ObtainedMarks, bool IsAbsent, bool IsWithheld,
        bool IsPublished, bool IsReconciled);

    public sealed record Mapping(long OutcomeId, long CurriculumSubjectId,
        decimal WeightPercent);

    public sealed record Outcome(long OutcomeId, decimal AttainmentPercent,
        int EvidenceCount, long PublicationId, int PublicationVersion);

    public static IReadOnlyList<Outcome> Calculate(
        IReadOnlyCollection<Evidence> evidence,
        IReadOnlyCollection<Mapping> mappings,
        int decimalPlaces = 2)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(mappings);
        if (decimalPlaces is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        if (evidence.Count == 0 || mappings.Count == 0)
            throw new ArgumentException("Published evidence and outcome mappings are required.");

        var rows = evidence.ToArray();
        if (rows.Any(x => x.PublicationId <= 0 || x.PublicationVersion <= 0 ||
            x.EnrollmentId <= 0 || x.CurriculumSubjectId <= 0 ||
            x.FullMarks <= 0 || x.ObtainedMarks < 0 || x.ObtainedMarks > x.FullMarks ||
            !x.IsPublished || !x.IsReconciled || x.IsWithheld || x.IsAbsent))
            throw new ArgumentException("Unpublished, unreconciled, absent, withheld or invalid evidence cannot establish attainment.");

        var publication = rows[0].PublicationId;
        var version = rows[0].PublicationVersion;
        if (rows.Any(x => x.PublicationId != publication || x.PublicationVersion != version))
            throw new ArgumentException("Evidence must belong to one immutable publication version.");
        if (rows.GroupBy(x => new { x.EnrollmentId, x.CurriculumSubjectId }).Any(g => g.Count() != 1))
            throw new ArgumentException("Duplicate learner/course evidence.");

        var mapRows = mappings.ToArray();
        if (mapRows.Any(x => x.OutcomeId <= 0 || x.CurriculumSubjectId <= 0 ||
            x.WeightPercent <= 0 || x.WeightPercent > 100))
            throw new ArgumentException("Invalid outcome mapping.");
        if (mapRows.GroupBy(x => new { x.OutcomeId, x.CurriculumSubjectId }).Any(g => g.Count() != 1))
            throw new ArgumentException("Duplicate outcome/course mapping.");

        var courseIds = rows.Select(x => x.CurriculumSubjectId).ToHashSet();
        if (mapRows.Any(x => !courseIds.Contains(x.CurriculumSubjectId)))
            throw new ArgumentException("Mapping has no published course evidence.");

        var results = new List<Outcome>();
        foreach (var group in mapRows.GroupBy(x => x.OutcomeId).OrderBy(x => x.Key))
        {
            if (group.Sum(x => x.WeightPercent) != 100m)
                throw new ArgumentException("Outcome weights must total exactly 100 percent.");
            decimal weighted = 0m;
            var count = 0;
            foreach (var mapping in group)
            {
                var course = rows.Where(x => x.CurriculumSubjectId == mapping.CurriculumSubjectId).ToArray();
                // Aggregate per course before applying its policy weight, so larger
                // class sizes do not silently change the configured mapping.
                var mean = course.Average(x => x.ObtainedMarks / x.FullMarks * 100m);
                weighted += mean * mapping.WeightPercent / 100m;
                count += course.Length;
            }
            results.Add(new Outcome(group.Key,
                decimal.Round(weighted, decimalPlaces, MidpointRounding.AwayFromZero),
                count, publication, version));
        }
        return results;
    }

    /// <summary>
    /// Calculates a learner's outcomes only when that learner has exactly one
    /// eligible published course record for every course mapped to each outcome.
    /// Never substitutes another learner's grade for missing evidence.
    /// </summary>
    public static IReadOnlyList<LearnerOutcome> CalculatePerLearner(
        IReadOnlyCollection<Evidence> evidence,
        IReadOnlyCollection<Mapping> mappings,
        int decimalPlaces = 2)
    {
        // Reuse all publication, grade, duplicate and mapping validation from
        // the existing cohort calculator before evaluating individual learners.
        _ = Calculate(evidence, mappings, decimalPlaces);
        var rows = evidence.ToArray();
        var mapRows = mappings.ToArray();
        var result = new List<LearnerOutcome>();
        foreach (var learner in rows.GroupBy(x => x.EnrollmentId).OrderBy(x => x.Key))
        {
            var courses = learner.ToDictionary(x => x.CurriculumSubjectId);
            foreach (var outcome in mapRows.GroupBy(x => x.OutcomeId).OrderBy(x => x.Key))
            {
                if (outcome.Any(x => !courses.ContainsKey(x.CurriculumSubjectId)))
                    throw new ArgumentException("Learner is missing published evidence for a mapped outcome course.");
                decimal total = 0m;
                foreach (var mapping in outcome)
                {
                    var grade = courses[mapping.CurriculumSubjectId];
                    total += (grade.ObtainedMarks / grade.FullMarks * 100m)
                        * mapping.WeightPercent / 100m;
                }
                result.Add(new LearnerOutcome(learner.Key, outcome.Key,
                    decimal.Round(total, decimalPlaces, MidpointRounding.AwayFromZero),
                    outcome.Count(), rows[0].PublicationId, rows[0].PublicationVersion));
            }
        }
        return result;
    }

    public sealed record LearnerOutcome(long EnrollmentId, long OutcomeId,
        decimal AttainmentPercent, int EvidenceCount,
        long PublicationId, int PublicationVersion);

}
