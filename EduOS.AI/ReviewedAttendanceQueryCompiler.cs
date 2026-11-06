using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>
/// Deterministic, offline Ask EduOS grammar. This only compiles a reviewed read-only
/// attendance metric; the canonical report executor must re-authorize every invocation.
/// No generated SQL, network calls, or model calls are permitted here.
/// </summary>
public static class ReviewedAttendanceQueryCompiler
{
    public const int GrammarVersion = 2;
    private static readonly Regex Tokens = new(@"[\p{L}\p{M}\p{N}-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IsoDate = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Attendance = new(StringComparer.Ordinal)
        { "attendance", "attendence", "হাজিরা", "উপস্থিতি", "hajira", "uposthiti" };
    private static readonly HashSet<string> Quantity = new(StringComparer.Ordinal)
        { "count", "number", "total", "কত", "কতজন", "কয়জন", "কয়জন", "সংখ্যা", "koto", "mot" };
    private static readonly HashSet<string> Present = new(StringComparer.Ordinal)
        { "present", "উপস্থিত", "uposthit" };
    private static readonly HashSet<string> Absent = new(StringComparer.Ordinal)
        { "absent", "অনুপস্থিত", "onuposthit" };
    private static readonly HashSet<string> Today = new(StringComparer.Ordinal)
        { "today", "আজ", "আজকে", "আজকের", "aj", "aaj", "ajke", "aajke" };
    private static readonly HashSet<string> Yesterday = new(StringComparer.Ordinal)
        { "yesterday", "গতকাল", "গতকালকে", "গতকালের", "gotokal", "gotokalke" };
    private static readonly HashSet<string> Glue = new(StringComparer.Ordinal)
        { "how", "many", "in", "students", "student", "were", "was", "are", "is", "the",
          "for", "on", "of", "please", "show", "me", "tell", "জন", "জনের", "শিক্ষার্থী",
          "ছাত্র", "ছাত্রী", "ছিল", "আছে", "হয়েছে", "হয়েছে", "দেখাও", "বল",
          "বলুন", "এর", "তে", "ক্লাসে", "ক্লাস" };

    public static CapabilityResult Compile(
        string? question, DateOnly localToday, AiRequestScope? scope, ReviewedCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!LocalFirstCapabilityPlanner.IsValidScope(scope))
            return CapabilityResult.Reject(CapabilityDecision.Denied, "Access denied.");
        if (string.IsNullOrWhiteSpace(question) || question.Length > 512 ||
            question.Any(char.IsControl))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Unsupported question.");

        var normalized = question.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        if (normalized.Contains(';') || normalized.Contains("--", StringComparison.Ordinal) ||
            normalized.Contains("/*", StringComparison.Ordinal) ||
            normalized.Contains("*/", StringComparison.Ordinal) ||
            normalized.Contains('=') || normalized.Contains('<') || normalized.Contains('>') ||
            normalized.Contains('`') || normalized.Contains('"') || normalized.Contains('\''))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Unsupported query syntax.");

        var remaining = Tokens.Replace(normalized, "");
        if (remaining.Any(c => !char.IsWhiteSpace(c) && c is not '?' and not '।' and not ',' and not '.' and not '!'))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Unsupported query syntax.");

        var words = Tokens.Matches(normalized).Select(m => m.Value).ToArray();
        if (words.Length is 0 or > 48 ||
            !words.Any(Attendance.Contains) || !words.Any(Quantity.Contains))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "No reviewed metric matched.");

        var present = words.Any(Present.Contains);
        var absent = words.Any(Absent.Contains);
        if (present == absent)
            return CapabilityResult.Reject(CapabilityDecision.ClarificationRequired, "Specify present or absent.");

        var dates = words.Where(w => IsoDate.IsMatch(w)).ToArray();
        var today = words.Any(Today.Contains);
        var yesterday = words.Any(Yesterday.Contains);
        if (dates.Length > 1 || (today && yesterday) || (dates.Length != 0 && (today || yesterday)))
            return CapabilityResult.Reject(CapabilityDecision.ClarificationRequired, "Specify one date only.");

        if (words.Any(w => !Attendance.Contains(w) && !Quantity.Contains(w) &&
                           !Present.Contains(w) && !Absent.Contains(w) &&
                           !Today.Contains(w) && !Yesterday.Contains(w) &&
                           !IsoDate.IsMatch(w) && !Glue.Contains(w)))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Unsupported filter or instruction.");

        DateOnly date;
        if (dates.Length == 1)
        {
            if (!DateOnly.TryParseExact(dates[0], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Invalid calendar date.");
        }
        else if (today || yesterday)
            date = yesterday ? localToday.AddDays(-1) : localToday;
        else
            return CapabilityResult.Reject(CapabilityDecision.ClarificationRequired, "Which date?");

        if (!registry.TryGet("attendance.status.count", out var capability) ||
            capability is null || capability.Version != 1 ||
            !string.Equals(capability.RegisteredMetricKey, "attendance.status.count.v1", StringComparison.Ordinal))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Reviewed report unavailable.");
        if (!scope!.Permissions.Contains(capability.RequiredPermission))
            return CapabilityResult.Reject(CapabilityDecision.Denied, "Access denied.");

        return new CapabilityResult(CapabilityDecision.Ready, capability.Key,
            new AttendanceCountQuerySpec(date,
                present ? AttendanceCountStatus.Present : AttendanceCountStatus.Absent), null);
    }
}
