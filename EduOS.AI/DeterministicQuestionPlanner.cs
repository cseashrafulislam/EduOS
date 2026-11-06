using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>
/// Pure, offline question planning. This component NEVER authorizes a request or executes a query.
/// The host must authenticate, enforce tenant/campus/row access, and resolve the registered metric
/// against an approved read service again at invocation time.
/// </summary>
public static class DeterministicQuestionPlanner
{
    public const int GrammarVersion = 1;
    public const int MaximumQuestionLength = 512;
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Date = new(@"(?<!\d)(?<date>\d{4}-\d{2}-\d{2})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] Attendance = ["attendance", "attendence", "উপস্থিতি", "হাজিরা", "uposthiti", "hajira"];
    private static readonly string[] Count = ["count", "number", "total", "কতজন", "কয়জন", "কয়জন", "কত", "সংখ্যা", "mot", "koto"];
    private static readonly string[] Today = ["today", "আজ", "আজকে", "আজকের", "aj", "aaj", "ajke", "aajke"];
    private static readonly string[] Yesterday = ["yesterday", "গতকাল", "গতকালকে", "গতকালের", "gotokal", "gotokalke"];
    private static readonly string[] Present = ["present", "উপস্থিত", "uposthit"];
    private static readonly string[] Absent = ["absent", "অনুপস্থিত", "onuposthit"];

    public static QuestionPlan Parse(string? question, DateOnly localToday)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaximumQuestionLength)
            return QuestionPlan.Unavailable("Question is empty or exceeds the supported length.");

        var normalized = Spaces.Replace(question.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToLowerInvariant(), " ");
        // Strictly reject syntax that may be interpreted as instructions, expressions or extra commands.
        if (normalized.Contains(';') || normalized.Contains("--", StringComparison.Ordinal) ||
            normalized.Contains("/*", StringComparison.Ordinal) || normalized.Contains("*/", StringComparison.Ordinal) ||
            normalized.Contains('=') || normalized.Contains('<') || normalized.Contains('>') ||
            normalized.Contains('\n') || normalized.Contains('\r'))
            return QuestionPlan.Unavailable("Unsupported query syntax.");

        if (!ContainsAny(normalized, Attendance) || !ContainsAny(normalized, Count))
            return QuestionPlan.Unavailable("No reviewed question pattern matched.");

        var present = ContainsAny(normalized, Present);
        var absent = ContainsAny(normalized, Absent);
        if (present == absent)
            return QuestionPlan.Clarify("Specify either present or absent attendance.");

        var dates = Date.Matches(normalized);
        if (dates.Count > 1)
            return QuestionPlan.Clarify("Only one date is supported per question.");

        DateOnly date;
        if (dates.Count == 1)
        {
            if (!DateOnly.TryParseExact(dates[0].Groups["date"].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return QuestionPlan.Unavailable("Invalid calendar date.");
            if (ContainsAny(normalized, Today) || ContainsAny(normalized, Yesterday))
                return QuestionPlan.Clarify("Specify one date expression only.");
        }
        else
        {
            var today = ContainsAny(normalized, Today);
            var yesterday = ContainsAny(normalized, Yesterday);
            if (today && yesterday)
                return QuestionPlan.Clarify("Specify one date expression only.");
            if (!today && !yesterday)
                return QuestionPlan.Clarify("Which attendance date? Use YYYY-MM-DD, today, or yesterday.");
            date = yesterday ? localToday.AddDays(-1) : localToday;
        }

        // The supported slice deliberately excludes arbitrary names, campuses, filters, rankings and joins.
        // Unknown extra tokens fail closed rather than being silently ignored.
        var remainder = normalized;
        foreach (var term in Attendance.Concat(Count).Concat(Present).Concat(Absent).Concat(Today).Concat(Yesterday)
                     .OrderByDescending(x => x.Length))
            remainder = Regex.Replace(remainder, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])",
                " ", RegexOptions.CultureInvariant);
        remainder = Date.Replace(remainder, " ");
        remainder = Regex.Replace(remainder, @"[?।,.!\s]", "", RegexOptions.CultureInvariant);
        if (remainder.Length != 0)
            return QuestionPlan.Unavailable("Unsupported filters or question wording.");

        return new QuestionPlan(PlanStatus.Ready, "attendance.status.count.v1",
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["status"] = present ? "present" : "absent"
            }), null);
    }

    private static bool ContainsAny(string input, IEnumerable<string> terms) =>
        terms.Any(term => Regex.IsMatch(input, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) +
            @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant));
}

public enum PlanStatus { Ready, ClarificationRequired, Unavailable }

public sealed record QuestionPlan(
    PlanStatus Status,
    string? RegisteredMetricKey,
    IReadOnlyDictionary<string, string> Slots,
    string? Message)
{
    public static QuestionPlan Clarify(string message) =>
        new(PlanStatus.ClarificationRequired, null, new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()), message);

    public static QuestionPlan Unavailable(string message) =>
        new(PlanStatus.Unavailable, null, new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()), message);
}
