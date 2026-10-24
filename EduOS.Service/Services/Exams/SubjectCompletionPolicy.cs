namespace EduOS.Service.Services.Exams;
/// <summary>Year-end completion is a separate audited decision, not a side effect of publishing one exam.</summary>
public readonly record struct SubjectCompletionEvidence(bool Published, bool SummaryPresent,
    bool Withheld, bool MarkPresent, bool Passed, bool ComponentsComplete);
public static class SubjectCompletionPolicy
{
    public static string? BlockReason(DateOnly yearEnd, DateOnly today,
        IReadOnlyList<SubjectCompletionEvidence> evidence)
    {
        if (yearEnd >= today) return "Academic year must end before completion.";
        if (evidence.Count == 0) return "No assessments are configured for this subject.";
        if (evidence.Any(x => !x.Published)) return "All subject assessments require published results.";
        if (evidence.Any(x => !x.SummaryPresent)) return "Published student result snapshot missing.";
        if (evidence.Any(x => x.Withheld)) return "Withheld marks or results prevent completion.";
        if (evidence.Any(x => !x.MarkPresent)) return "Student assessment marks missing.";
        if (evidence.Any(x => !x.Passed)) return "Student did not pass every subject assessment.";
        if (evidence.Any(x => !x.ComponentsComplete)) return "Component marks or weightage incomplete.";
        return null;
    }
}
