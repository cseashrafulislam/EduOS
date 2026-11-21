using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>
/// External model fallback is restricted to general learning questions. No institution
/// records, identified people, SQL, selected tenant/campus or source tables are sent.
/// The recognized institutional metrics must always be answered by authorized MIS.
/// </summary>
public static class GeneralKnowledgeQuestionPolicy
{
    private const string PrivateEnglish =
        @"\b(?:my|our|mine|ours|your|school|institution|campus|tenant|database|records?|" +
        @"students?|pupils?|employees?|teachers?|guardians?|parents?|salary|payroll|" +
        @"fees?|dues?|invoices?|payments?|transactions?|attendance|admissions?|" +
        @"enrollments?|balances?|expenses?|income|revenue|financial|ledger|" +
        @"grades?|marks?|results?|reports?|accounts?|section|batch|staff|class|" +
        @"finance|cash|budget|bank|stocks?|payables?|receivables?|assets?|" +
        @"passwords?|secrets?|tokens?|api\s+keys?|credentials?|otps?|pins?)\b";

    private static readonly string[] PrivateBangla =
    {
        "আমার", "আমাদের", "আপনার", "প্রতিষ্ঠান", "স্কুল", "বিদ্যালয়", "বিদ্যালয়",
        "ক্যাম্পাস", "শিক্ষার্থী", "ছাত্র", "ছাত্রী", "শিক্ষক", "কর্মচারী", "অভিভাবক",
        "ফি", "বকেয়া", "বকেয়া", "বেতন", "পে-রোল", "ব্যালেন্স", "আয়", "আয়", "ব্যয়", "ব্যয়",
        "হাজিরা", "উপস্থিতি", "ভর্তি", "পরীক্ষা", "ফলাফল", "মার্কস", "রিপোর্ট",
        "লেনদেন", "ইনভয়েস", "ইনভয়েস", "সেকশন", "ব্যাচ",
        "ব্যাংক", "টাকা", "বাজেট", "স্টক", "মজুত", "পাসওয়ার্ড", "পাসওয়ার্ড", "গোপন", "টোকেন"
    };

    public static bool IsAllowed(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length is < 5 or > 200 ||
            question.Any(char.IsControl)) return false;

        var text = question.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        if (Regex.IsMatch(text, PrivateEnglish, RegexOptions.CultureInvariant) ||
            PrivateBangla.Any(text.Contains) ||
            Regex.IsMatch(text, @"(?:https?://|www\.|[^\s@]+@[^\s@]+\.[^\s@]+|\b\d{6,}\b|--|/\*|\*/|;|[<>])",
                RegexOptions.CultureInvariant))
            return false;

        return Regex.IsMatch(text,
            @"\b(?:what|why|how|explain|define|describe|compare|difference|meaning|concept|overview)\b|" +
            @"(?:কী|কি|কেন|কিভাবে|কীভাবে|ব্যাখ্যা|পার্থক্য|বোঝাও|বুঝিয়ে|বুঝিয়ে|শেখাও)",
            RegexOptions.CultureInvariant);
    }
}
