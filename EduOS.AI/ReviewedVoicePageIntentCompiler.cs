using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>
/// One reviewed, provider-independent UI intent contract shared by all EduOS pages.
/// The supplied page controls are UNTRUSTED presentation hints, never permissions.
/// This compiler neither reads tenant data nor writes forms, reports or ERP records.
/// </summary>
public sealed class VoicePagePreviewRequest
{
    public string Utterance { get; set; } = string.Empty;
    public string PagePath { get; set; } = string.Empty;
    public List<VoicePageTarget> Targets { get; set; } = new();
}

public sealed class VoicePageTarget
{
    public string Key { get; set; } = string.Empty;
    // text, filter, select, or readonly. All may be focused when visible.
    public string Kind { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<VoicePageOption> Options { get; set; } = new();
}

public sealed class VoicePageOption
{
    public string Key { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
}

public enum VoicePageDecision { Ready, ClarificationRequired, Unavailable }

public sealed record VoicePagePlan(
    VoicePageDecision Decision, string Message, string? Action = null,
    string? TargetKey = null, string? ProposedValue = null, string? OptionKey = null)
{
    // This is a proposal, not authorization or execution of a UI/ERP mutation.
    public bool RequiresConfirmation => Decision == VoicePageDecision.Ready;
    public bool BusinessWriteExecuted => false;
}

public static class ReviewedVoicePageIntentCompiler
{
    public const int ContractVersion = 1;
    public const int MaximumUtteranceLength = 512;
    public const int MaximumTargets = 40;
    public const int MaximumOptionsPerTarget = 60;

    private static readonly Regex PathPattern = new(
        @"^/(?:[a-z0-9_-]+(?:/[a-z0-9_-]+)*)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(
        @"^[a-z][a-z0-9_.-]{0,79}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeField = new(
        @"password|token|secret|salary|payroll|payment|amount|price|fee|grade|marks|attendance|stock|inventory|approval|posting|bank|account|nid|national.?id|birth|phone|mobile|email|address|student.?id|employee.?id|guardian.?id|পাসওয়ার্ড|পাসওয়ার্ড|গোপন|বেতন|পেরোল|টাকা|মূল্য|ফি|মার্কস|নম্বর|গ্রেড|উপস্থিতি|স্টক|ইনভেন্টরি|অনুমোদন|পোস্টিং|ব্যাংক|ব্যাঙ্ক|হিসাব|জাতীয়.?পরিচয়|জাতীয়.?পরিচয়|জন্ম|ফোন|মোবাইল|ইমেইল|ই-মেইল|ঠিকানা|শিক্ষার্থী.?আইডি|অভিভাবক.?আইডি|কর্মচারী.?আইডি",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishDraft = new(
        @"^(?:please\s+)?(?:draft|prefill|fill)\s+(.+?)\s+(?:with|as)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BanglaDraft = new(
        @"^(.+?)\s+(?:লিখুন|বসান|দিন)\s*[:：]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishFilter = new(
        @"^(?:please\s+)?(?:filter|search)\s+(.+?)\s+(?:by|for|contains)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BanglaFilter = new(
        @"^(.+?)\s+খুঁজুন\s*[:：]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishSelect = new(
        @"^(?:please\s+)?(?:select|choose)\s+(.+?)\s+(?:as|to|value)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BanglaSelect = new(
        @"^(.+?)\s+(?:নির্বাচন করুন|বেছে নিন)\s*[:：]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishFocus = new(
        @"^(?:please\s+)?(?:show|open|find|focus)\s+(.+?)(?:\s+field)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BanglaFocus = new(
        @"^(.+?)\s+(?:ঘর\s+)?(?:দেখাও|দেখান|খুলুন|খোলো)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static VoicePagePlan Preview(VoicePagePreviewRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Utterance) ||
            request.Utterance.Length > MaximumUtteranceLength ||
            HasBadCharacters(request.Utterance) || !ValidPath(request.PagePath) ||
            request.Targets is null || request.Targets.Count is < 1 or > MaximumTargets ||
            !ValidTargets(request.Targets))
            return Unavailable("Invalid or unsupported page command metadata.");

        var utterance = request.Utterance.Normalize(NormalizationForm.FormKC).Trim();
        var parsed = Parse(utterance);
        if (parsed is null)
            return Unavailable("Only reviewed field, filter and selection commands are supported.");

        var (action, requestedLabel, proposedValue) = parsed.Value;
        if (!ValidLabel(requestedLabel))
            return Unavailable("Specify a valid field label.");

        // Never use a substring/first-match heuristic on an ambiguous control.
        var matches = request.Targets.Where(t =>
            t.Labels.Any(label => Normalize(label) == Normalize(requestedLabel))).ToArray();
        if (matches.Length > 1)
            return Clarify("More than one field has this label. Specify an unambiguous label.");
        if (matches.Length == 0)
            return Unavailable("This field is unavailable in the supplied page context.");

        var target = matches[0];
        // Server cannot attest DOM visibility. The per-page adapter must recheck
        // live authorization, visible controls and dependent option state at apply.
        if (UnsafeField.IsMatch(target.Key) || target.Labels.Any(UnsafeField.IsMatch))
            return Unavailable("This field cannot be prepared by voice.");

        if (action == "focus")
            return new VoicePagePlan(VoicePageDecision.Ready,
                "Review the target on the current page.", "FocusField", target.Key);

        if (action == "draft")
        {
            if (target.Kind != "text" || !ValidValue(proposedValue, 120))
                return Unavailable("Only eligible plain-text fields support drafts.");
            return new VoicePagePlan(VoicePageDecision.Ready,
                "Review the suggested text before applying it.", "DraftText", target.Key, proposedValue);
        }
        if (action == "filter")
        {
            if (target.Kind != "filter" || !ValidValue(proposedValue, 80))
                return Unavailable("Only eligible search filters support this command.");
            return new VoicePagePlan(VoicePageDecision.Ready,
                "Review the filter before applying it.", "SetFilter", target.Key, proposedValue);
        }
        if (action != "select" || target.Kind != "select" ||
            !ValidValue(proposedValue, 120))
            return Unavailable("Only explicitly eligible dropdowns support selection previews.");

        var options = target.Options.Where(o =>
            o.Labels.Any(label => Normalize(label) == Normalize(proposedValue))).ToArray();
        if (options.Length > 1)
            return Clarify("More than one option matches. Choose a unique option label.");
        if (options.Length == 0)
            return Unavailable("Option not present. Load the current dropdown choices first.");
        return new VoicePagePlan(VoicePageDecision.Ready,
            "Review the proposed dropdown choice; dependent choices may change.",
            "SelectOption", target.Key, proposedValue, options[0].Key);
    }

    private static (string Action, string Label, string Value)? Parse(string utterance)
    {
        foreach (var item in new (Regex Pattern, string Action)[]
        {
            (EnglishDraft, "draft"), (BanglaDraft, "draft"),
            (EnglishFilter, "filter"), (BanglaFilter, "filter"),
            (EnglishSelect, "select"), (BanglaSelect, "select"),
            (EnglishFocus, "focus"), (BanglaFocus, "focus")
        })
        {
            var match = item.Pattern.Match(utterance);
            if (match.Success)
                return (item.Action, match.Groups[1].Value.Trim(),
                    match.Groups.Count > 2 ? match.Groups[2].Value.Trim() : "");
        }
        return null;
    }

    private static bool ValidTargets(List<VoicePageTarget> targets)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (target is null || !ValidKey(target.Key) || !keys.Add(target.Key) ||
                target.Kind is not ("text" or "filter" or "select" or "readonly") ||
                target.Labels is null || target.Labels.Count is < 1 or > 8 ||
                target.Labels.Any(l => !ValidLabel(l)) ||
                target.Options is null || target.Options.Count > MaximumOptionsPerTarget ||
                (target.Kind != "select" && target.Options.Count != 0))
                return false;

            var optionKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in target.Options)
                if (option is null || !ValidKey(option.Key) || !optionKeys.Add(option.Key) ||
                    option.Labels is null || option.Labels.Count is < 1 or > 8 ||
                    option.Labels.Any(l => !ValidLabel(l)))
                    return false;
        }
        return true;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool ValidPath(string? value) =>
        value is { Length: > 0 and <= 180 } && PathPattern.IsMatch(value);
    private static bool ValidKey(string? value) =>
        value is { Length: > 0 and <= 80 } && KeyPattern.IsMatch(value);
    private static bool ValidLabel(string? value) =>
        value is { Length: >= 2 and <= 100 } && !HasBadCharacters(value);
    private static bool ValidValue(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !HasBadCharacters(value);
    private static bool HasBadCharacters(string? value) =>
        value is null || value.Any(c => char.IsControl(c) || c is '<' or '>');
    private static VoicePagePlan Unavailable(string message) =>
        new(VoicePageDecision.Unavailable, message);
    private static VoicePagePlan Clarify(string message) =>
        new(VoicePageDecision.ClarificationRequired, message);
}
