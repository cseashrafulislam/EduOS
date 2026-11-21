using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>Exact bilingual question-to-metric routing. No user SQL, tenant, campus or date inference.</summary>
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
        ["open work orders"] = "open_work_orders", ["খোলা ওয়ার্ক অর্ডার"] = "open_work_orders",
        ["issued library loans"] = "issued_loans", ["ইস্যুকৃত লাইব্রেরি বই"] = "issued_loans"
    };

    public static string? Parse(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 120 ||
            question.Any(char.IsControl)) return null;
        var text = Regex.Replace(question.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ")
            .TrimEnd('?', '？', '।', '.').Trim().ToLowerInvariant();
        return Aliases.TryGetValue(text, out var metric) ? metric : null;
    }
}
