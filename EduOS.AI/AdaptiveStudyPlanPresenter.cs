namespace EduOS.AI;

/// <summary>
/// Pure, model-off learner presentation of a canonical published-assessment study plan.
/// This formatter never reads grades, authorizes records or sends data to a provider.
/// Only a Ready result from AdaptiveStudyPlanPolicy may be displayed.
/// </summary>
public static class AdaptiveStudyPlanPresenter
{
    public sealed record LocalizedSkill(
        string SkillId, string Priority, string Feedback,
        decimal ObservedPercent, int PublishedAttemptCount);

    public sealed record LocalizedPlan(
        AdaptiveStudyPlanPolicy.PlanStatus Status, string Message,
        IReadOnlyList<LocalizedSkill> Skills);

    public static LocalizedPlan Present(
        AdaptiveStudyPlanPolicy.StudyPlanResult? result, string? locale)
    {
        var bangla = string.Equals(locale, "bn-BD", StringComparison.OrdinalIgnoreCase);
        if (!bangla && !string.Equals(locale, "en-BD", StringComparison.OrdinalIgnoreCase))
            return new(AdaptiveStudyPlanPolicy.PlanStatus.Invalid,
                "Unsupported language.", Array.Empty<LocalizedSkill>());

        if (result is null)
            return new(AdaptiveStudyPlanPolicy.PlanStatus.Invalid,
                bangla ? "পড়াশোনার পরিকল্পনা পাওয়া যায়নি।" : "Study plan unavailable.",
                Array.Empty<LocalizedSkill>());

        if (result.Status != AdaptiveStudyPlanPolicy.PlanStatus.Ready)
        {
            var message = result.Status switch
            {
                AdaptiveStudyPlanPolicy.PlanStatus.Denied =>
                    bangla ? "এই ফলাফল দেখার অনুমতি নেই।" : "Access to these results is denied.",
                AdaptiveStudyPlanPolicy.PlanStatus.NoEvidence =>
                    bangla ? "প্রকাশিত মূল্যায়নের তথ্য পাওয়া যায়নি।" : "No published assessment evidence.",
                _ => bangla ? "পরিকল্পনার তথ্য সঠিক নয়।" : "Invalid study plan evidence."
            };
            return new(result.Status, message, Array.Empty<LocalizedSkill>());
        }

        // The presenter must not turn a malformed upstream payload into a trusted
        // learner recommendation or disclose partial data from a mixed result.
        if (result.Skills is null || result.Skills.Count == 0 || result.Skills.Count > 20 ||
            result.Skills.Any(x => x is null ||
                string.IsNullOrWhiteSpace(x.SkillId) || x.SkillId.Length > 128 ||
                x.SkillId.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
                    char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
                    c is '<' or '>') ||
                x.ObservedPercent is null or < 0 or > 100 ||
                x.PublishedAttemptCount < 1 ||
                x.Priority != (x.ObservedPercent < 50m ? AdaptiveStudyPlanPolicy.StudyPriority.Review :
                    x.ObservedPercent < 80m ? AdaptiveStudyPlanPolicy.StudyPriority.Practice :
                    AdaptiveStudyPlanPolicy.StudyPriority.Extend) ||
                x.FeedbackCode != (x.Priority switch
                {
                    AdaptiveStudyPlanPolicy.StudyPriority.Review => "review_chapter",
                    AdaptiveStudyPlanPolicy.StudyPriority.Practice => "practice_skill",
                    AdaptiveStudyPlanPolicy.StudyPriority.Extend => "extend_learning",
                    _ => ""
                })))
            return new(AdaptiveStudyPlanPolicy.PlanStatus.Invalid,
                bangla ? "পরিকল্পনার তথ্য সঠিক নয়।" : "Invalid study plan evidence.",
                Array.Empty<LocalizedSkill>());

        // One canonical skill must appear only once; conflicting duplicates are not safe to render.
        if (result.Skills.Select(x => x.SkillId).Distinct(StringComparer.Ordinal).Count() != result.Skills.Count)
            return new(AdaptiveStudyPlanPolicy.PlanStatus.Invalid,
                bangla ? "পরিকল্পনার তথ্য সঠিক নয়।" : "Invalid study plan evidence.",
                Array.Empty<LocalizedSkill>());

        var skills = result.Skills.Select(x =>
        {
            var (priority, feedback) = x.Priority switch
            {
                AdaptiveStudyPlanPolicy.StudyPriority.Review => bangla
                    ? ("পুনরায় পড়ুন", "এই দক্ষতার অধ্যায়টি আবার পড়ুন এবং অনুমোদিত অনুশীলন করুন।")
                    : ("Review", "Revisit this chapter and practise with assigned material."),
                AdaptiveStudyPlanPolicy.StudyPriority.Practice => bangla
                    ? ("অনুশীলন", "এই দক্ষতার বিদ্যমান LMS অনুশীলনগুলো সম্পন্ন করুন।")
                    : ("Practice", "Complete the existing LMS practice for this skill."),
                _ => bangla
                    ? ("আরও শিখুন", "এই দক্ষতার পরবর্তী পাঠ পড়ুন।")
                    : ("Extend", "Continue to the next lesson for this skill.")
            };
            return new LocalizedSkill(x.SkillId, priority, feedback,
                x.ObservedPercent!.Value, x.PublishedAttemptCount);
        }).ToArray();

        return new(result.Status,
            bangla ? "প্রকাশিত মূল্যায়নের ভিত্তিতে পড়াশোনার পরামর্শ।" :
                "Study guidance based on published assessments.", skills);
    }
}
