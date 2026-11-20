using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

public enum ManagementMetric
{
    ActiveEnrollments, PresentEntries, AbsentEntries, LateEntries,
    LeaveEntries, ExcusedEntries, FinalizedEntries, AttendanceRate
}

public sealed record ManagementQuestionPlan(ManagementMetric? Metric, string? Message)
{
    public static ManagementQuestionPlan Reject(string message) => new(null, message);
}

// Local-first and deliberately narrow. User text never selects a tenant, SQL, table or report.
public static class ManagementQuestionPlanner
{
    private static bool Word(string text, string pattern) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])(?:" + pattern + @")(?![\p{L}\p{N}])",
            RegexOptions.CultureInvariant);

    public static ManagementQuestionPlan Parse(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 200 ||
            question.Any(char.IsControl))
            return ManagementQuestionPlan.Reject("Use a short question about attendance or active enrollments.");
        var value = question.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        if (Regex.IsMatch(value, @"(?:--|/\*|\*/|;|[<>]|\b(?:sql|select|delete|update|drop|ignore|salary|fee|grade|finance|bank|other\s+campus|other\s+tenant|class|section|batch)\b)",
                RegexOptions.CultureInvariant) ||
            Regex.IsMatch(value, @"\b\d{4}-\d{2}-\d{2}\b") ||
            Word(value, "today|yesterday|tomorrow|last|next") ||
            new[] { "আজ", "গতকাল", "আগামীকাল", "এই মাস", "শ্রেণি", "সেকশন", "ব্যাচ", "ক্যাম্পাস", "বেতন", "ফি" }
                .Any(value.Contains))
            return ManagementQuestionPlan.Reject("Use the authorized campus/date filters; unsupported filters cannot be interpreted.");

        var absent = Word(value, "absent|absence") || value.Contains("অনুপস্থিত");
        var normalizedWithoutAbsent = value.Replace("অনুপস্থিত", "");
        var present = Word(value, "present") || normalizedWithoutAbsent.Contains("উপস্থিত");
        var late = Word(value, "late") || value.Contains("বিলম্ব") || value.Contains("দেরি");
        var leave = Word(value, "leave") || value.Contains("ছুটি");
        var excused = Word(value, "excused") || value.Contains("অব্যাহতি");
        var enrollment = Word(value, "enrollments?|enrolments?|enrolled|admissions?") ||
                         value.Contains("ভর্তি");
        var rate = Word(value, "rate|percentage|percent") || value.Contains("হার") || value.Contains("শতকরা");
        var total = Word(value, "total\s+attendance|attendance\s+entries|finalized\s+entries") ||
                    value.Contains("মোট হাজিরা");
        if (rate && present && !absent && !late && !leave && !excused && !enrollment)
            present = false;
        var metrics = new List<ManagementMetric>();
        if (enrollment) metrics.Add(ManagementMetric.ActiveEnrollments);
        if (absent) metrics.Add(ManagementMetric.AbsentEntries);
        if (present) metrics.Add(ManagementMetric.PresentEntries);
        if (late) metrics.Add(ManagementMetric.LateEntries);
        if (leave) metrics.Add(ManagementMetric.LeaveEntries);
        if (excused) metrics.Add(ManagementMetric.ExcusedEntries);
        if (rate) metrics.Add(ManagementMetric.AttendanceRate);
        if (total) metrics.Add(ManagementMetric.FinalizedEntries);
        if (metrics.Count != 1)
            return ManagementQuestionPlan.Reject(metrics.Count == 0
                ? "Supported: active enrollments, present, absent, late, leave, excused, finalized entries or attendance rate."
                : "Ask for exactly one metric, with campus and dates selected separately.");
        return new ManagementQuestionPlan(metrics[0], null);
    }
}
