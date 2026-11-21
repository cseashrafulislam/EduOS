using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>Strict bilingual question-to-metric routing; catalogue contains only public metadata, never tenant data.</summary>
public sealed record InstitutionQuestionSuggestion(string Question, string MetricKey);

public static class InstitutionMisQuestionPlanner
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["active students"] = "active_students", ["সক্রিয় শিক্ষার্থী"] = "active_students",
        ["সক্রিয় শিক্ষার্থী"] = "active_students", ["active employees"] = "active_employees",
        ["সক্রিয় কর্মচারী"] = "active_employees", ["সক্রিয় কর্মচারী"] = "active_employees",
        ["approved unposted payroll"] = "approved_unposted_payroll_runs",
        ["পোস্ট না হওয়া অনুমোদিত বেতন"] = "approved_unposted_payroll_runs",
        ["outstanding fee amount"] = "outstanding_fee_amount",
        ["unpaid fee amount"] = "outstanding_fee_amount", ["fee dues"] = "outstanding_fee_amount",
        ["বকেয়া ফি"] = "outstanding_fee_amount", ["বকেয়া ফি"] = "outstanding_fee_amount",
        ["outstanding fee invoices"] = "outstanding_fee_invoices",
        ["unpaid fee invoices"] = "outstanding_fee_invoices",
        ["বকেয়া ফি ইনভয়েস"] = "outstanding_fee_invoices",
        ["posted income"] = "posted_income", ["পোস্টেড আয়"] = "posted_income",
        ["posted expenses"] = "posted_expenses", ["পোস্টেড ব্যয়"] = "posted_expenses",
        ["net surplus"] = "net_surplus", ["নিট উদ্বৃত্ত"] = "net_surplus",
        ["finalized absences"] = "finalized_student_absences",
        ["finalized attendance sessions"] = "finalized_attendance_sessions",
        ["চূড়ান্ত অনুপস্থিতি"] = "finalized_student_absences",
        ["চূড়ান্ত হাজিরা সেশন"] = "finalized_attendance_sessions",
        ["published result versions"] = "published_result_versions",
        ["প্রকাশিত ফলাফলের সংস্করণ"] = "published_result_versions",
        ["open work orders"] = "open_work_orders", ["খোলা ওয়ার্ক অর্ডার"] = "open_work_orders",
        ["issued library loans"] = "issued_loans", ["ইস্যুকৃত লাইব্রেরি বই"] = "issued_loans",
        ["submitted requisitions"] = "submitted_requisitions", ["জমা দেওয়া রিকুইজিশন"] = "submitted_requisitions",
        ["purchase orders awaiting review"] = "purchase_orders_awaiting_review",
        ["পর্যালোচনার অপেক্ষায় ক্রয় আদেশ"] = "purchase_orders_awaiting_review",
        ["active assets"] = "active_assets", ["সক্রিয় সম্পদ"] = "active_assets",
        ["active hostel allocations"] = "active_hostel_allocations",
        ["সক্রিয় হোস্টেল বরাদ্দ"] = "active_hostel_allocations",
        ["visitor policy changes awaiting review"] = "visitor_policy_changes_awaiting_review",
        ["পর্যালোচনার অপেক্ষায় ভিজিটর নীতি"] = "visitor_policy_changes_awaiting_review",
        ["planned transport trips"] = "planned_trips", ["পরিকল্পিত পরিবহন ট্রিপ"] = "planned_trips"
    };

    // Bengali composed/decomposed letter forms must match identically on both
    // sides of the alias index; dictionary literals are not normalized by default.
    private static readonly IReadOnlyDictionary<string, string> NormalizedAliases =
        Aliases.GroupBy(x =>
            Regex.Replace(x.Key.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant(),
                @"\s+", " "), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.Ordinal);

    private static readonly string[] Prefixes =
    {
        "how many ", "how much ", "number of ", "count of ", "show ", "please show ",
        "total ", "what is ", "কতজন ", "কতটি ", "কত টাকা ", "মোট ", "দেখাও ", "আমাকে দেখাও "
    };

    private static readonly string[] Suffixes =
    {
        " count", " total", " amount", " কতজন", " কতটি", " কত টাকা", " কত", " সংখ্যা", " দেখাও", " জানাও"
    };

    /// <summary>Metadata-only suggestions. Real values still require authenticated, authorized MIS calls.</summary>
    public static IReadOnlyList<InstitutionQuestionSuggestion> Suggestions(string? search = null)
    {
        if (search?.Length > 80 || search?.Any(char.IsControl) == true)
            return Array.Empty<InstitutionQuestionSuggestion>();
        var filter = (search ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        return Aliases
            .Where(x => filter.Length == 0 || x.Key.Contains(filter, StringComparison.Ordinal) ||
                x.Value.Contains(filter, StringComparison.Ordinal))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Take(100)
            .Select(x => new InstitutionQuestionSuggestion(x.Key, x.Value))
            .ToArray();
    }

    public static string? Parse(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 120 ||
            question.Any(char.IsControl)) return null;
        var text = Regex.Replace(question.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ")
            .TrimEnd('?', '？', '।', '.').Trim().ToLowerInvariant();
        // Strip only known count/display words. Never infer a period, campus, student,
        // role or unrestricted SQL filter from free text.
        for (var i = 0; i < 4; i++)
        {
            if (NormalizedAliases.TryGetValue(text, out var metric)) return metric;
            var prefix = Prefixes.FirstOrDefault(x => text.StartsWith(x, StringComparison.Ordinal));
            if (prefix != null)
            {
                text = text[prefix.Length..].Trim();
                continue;
            }
            var suffix = Suffixes.FirstOrDefault(x => text.EndsWith(x, StringComparison.Ordinal));
            if (suffix != null)
            {
                text = text[..^suffix.Length].Trim();
                continue;
            }
            break;
        }
        return NormalizedAliases.TryGetValue(text, out var result) ? result : null;
    }
}
