namespace EduOS.AI;

/// <summary>
/// Read-only study recommendations from already published, authorized LMS assessment
/// evidence. The caller must obtain rows from canonical LMS/Assessment services.
/// This policy never writes grades, attempts, questions, or learner profiles.
/// </summary>
public static class AdaptiveStudyPlanPolicy
{
    public sealed record PublishedAttemptEvidence(
        string TenantId, string CampusId, string LearnerId,
        string AssessmentId, string ChapterId, string SkillId,
        decimal EarnedMarks, decimal PossibleMarks,
        DateTimeOffset PublishedAtUtc, bool LearnerMayView, bool IsPublished);

    public sealed record StudyPlanRequest(
        string TenantId, string CampusId, string LearnerId,
        string ChapterId, int MaxSkills = 5);

    public enum PlanStatus { Ready, NoEvidence, Denied, Invalid }
    public enum StudyPriority { Review, Practice, Extend }

    public sealed record SkillRecommendation(
        string SkillId, StudyPriority Priority, decimal? ObservedPercent,
        int PublishedAttemptCount, string FeedbackCode);

    public sealed record StudyPlanResult(
        PlanStatus Status, IReadOnlyList<SkillRecommendation> Skills)
    {
        public static StudyPlanResult Reject(PlanStatus status) =>
            new(status, Array.Empty<SkillRecommendation>());
    }

    public static StudyPlanResult Build(
        StudyPlanRequest? request,
        IReadOnlyCollection<PublishedAttemptEvidence>? authorizedPublishedAttempts)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.CampusId) ||
            string.IsNullOrWhiteSpace(request.LearnerId))
            return StudyPlanResult.Reject(PlanStatus.Denied);

        if (string.IsNullOrWhiteSpace(request.ChapterId) ||
            request.MaxSkills is < 1 or > 20)
            return StudyPlanResult.Reject(PlanStatus.Invalid);

        if (authorizedPublishedAttempts is null || authorizedPublishedAttempts.Count == 0)
            return StudyPlanResult.Reject(PlanStatus.NoEvidence);

        // A mixed or unpublished result set indicates an unsafe source adapter.
        // Never silently discard unauthorized rows and continue with the remainder.
        if (authorizedPublishedAttempts.Any(row => row is null ||
            !row.LearnerMayView || !row.IsPublished ||
            row.TenantId != request.TenantId ||
            row.CampusId != request.CampusId ||
            row.LearnerId != request.LearnerId ||
            row.ChapterId != request.ChapterId))
            return StudyPlanResult.Reject(PlanStatus.Denied);

        if (authorizedPublishedAttempts.Any(row =>
            string.IsNullOrWhiteSpace(row.AssessmentId) ||
            string.IsNullOrWhiteSpace(row.SkillId) ||
            row.PublishedAtUtc == default ||
            row.PossibleMarks <= 0 ||
            row.EarnedMarks < 0 ||
            row.EarnedMarks > row.PossibleMarks))
            return StudyPlanResult.Reject(PlanStatus.Invalid);

        // Multiple official attempts remain separate evidence; no grade mutation.
        // Aggregate only already-published marks and preserve missing-data semantics.
        var skills = authorizedPublishedAttempts
            .GroupBy(row => row.SkillId, StringComparer.Ordinal)
            .Select(group =>
            {
                var possible = group.Sum(row => row.PossibleMarks);
                var earned = group.Sum(row => row.EarnedMarks);
                var percent = Math.Round(earned * 100m / possible, 2,
                    MidpointRounding.AwayFromZero);
                var priority = percent < 50m ? StudyPriority.Review :
                    percent < 80m ? StudyPriority.Practice : StudyPriority.Extend;
                return new SkillRecommendation(group.Key, priority, percent,
                    group.Count(), priority switch
                    {
                        StudyPriority.Review => "review_chapter",
                        StudyPriority.Practice => "practice_skill",
                        _ => "extend_learning"
                    });
            })
            .OrderBy(item => item.ObservedPercent)
            .ThenBy(item => item.SkillId, StringComparer.Ordinal)
            .Take(request.MaxSkills)
            .ToArray();

        return new StudyPlanResult(PlanStatus.Ready, skills);
    }
}
