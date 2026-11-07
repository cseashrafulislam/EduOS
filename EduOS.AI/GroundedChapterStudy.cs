using System.Text;
using System.Text.RegularExpressions;

namespace EduOS.AI;

/// <summary>
/// Model-off, citation-only study answers from chapter excerpts already authorized by
/// the canonical LMS/Library source. This service never fetches books or calls providers.
/// The host MUST authorize the learner and each edition/chapter before supplying excerpts.
/// </summary>
public static class GroundedChapterStudy
{
    public const int MaxQuestionLength = 300;
    public const int MaxExcerptLength = 1200;
    public const int MaxEvidenceRows = 40;
    public const int MaxCitationLength = 160;
    private static readonly Regex Words = new(@"[\p{L}\p{M}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record AuthorizedChapterExcerpt(
        string TenantId, string CampusId, string BookId, string EditionId,
        string ChapterId, string Citation, string Text, bool LicensedForDisplay,
        bool LearnerCanRead);

    public sealed record StudyRequest(
        string Question, string TenantId, string CampusId, string BookId,
        string EditionId, string ChapterId, string UserId);

    public enum Outcome { Answered, NoEvidence, Denied, Invalid }
    public sealed record StudyAnswer(Outcome Status, string Message, string? Citation)
    {
        public static StudyAnswer Reject(Outcome status) => new(status,
            status == Outcome.Denied ? "Access denied." :
            status == Outcome.Invalid ? "Invalid study request." :
            "No supporting passage was found in the selected chapter.", null);
    }

    /// <summary>
    /// Evidence is returned verbatim only as a SHORT citation excerpt, not a generated
    /// claim. The authorized source provider controls rights and per-record visibility.
    /// No transcript, learner identifier, or source content is persisted.
    /// </summary>
    public static StudyAnswer Answer(StudyRequest? request,
        IReadOnlyCollection<AuthorizedChapterExcerpt>? authorizedExcerpts)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.CampusId))
            return StudyAnswer.Reject(Outcome.Denied);
        if (string.IsNullOrWhiteSpace(request.Question) ||
            request.Question.Length > MaxQuestionLength ||
            request.Question.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
                char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) ||
            string.IsNullOrWhiteSpace(request.BookId) ||
            string.IsNullOrWhiteSpace(request.EditionId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
            return StudyAnswer.Reject(Outcome.Invalid);
        if (authorizedExcerpts is null || authorizedExcerpts.Count == 0)
            return StudyAnswer.Reject(Outcome.NoEvidence);
        if (authorizedExcerpts.Count > MaxEvidenceRows)
            return StudyAnswer.Reject(Outcome.Invalid);

        var question = request.Question.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        if (question.Contains("ignore previous", StringComparison.Ordinal) ||
            question.Contains("system prompt", StringComparison.Ordinal) ||
            question.Contains("developer message", StringComparison.Ordinal) ||
            question.Contains("নির্দেশনা উপেক্ষা", StringComparison.Ordinal) ||
            question.Contains("প্রম্পট", StringComparison.Ordinal))
            return StudyAnswer.Reject(Outcome.Invalid);

        // Stop words do not create evidence. Unknown terms must match source words;
        // this intentionally abstains rather than hallucinating explanations.
        var stop = new HashSet<string>(StringComparer.Ordinal) {
            "what", "why", "how", "is", "are", "the", "a", "an", "of", "in",
            "explain", "describe", "please", "chapter", "about", "কি", "কী",
            "কেন", "কিভাবে", "কীভাবে", "ব্যাখ্যা", "কর", "করো", "করুন", "এর",
            "এই", "অধ্যায়", "অধ্যায়", "সম্পর্কে"
        };
        var terms = Words.Matches(question).Select(x => x.Value)
            .Where(x => x.Length > 1 && !stop.Contains(x))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length == 0)
            return StudyAnswer.Reject(Outcome.NoEvidence);

        // Fail closed if ANY supplied record is out-of-scope or unlicensed.
        // Do not allow a mixed authorized/unauthorized result set to leak.
        if (authorizedExcerpts.Any(e => e is null ||
            !e.LearnerCanRead || !e.LicensedForDisplay ||
            e.TenantId != request.TenantId || e.CampusId != request.CampusId ||
            e.BookId != request.BookId || e.EditionId != request.EditionId ||
            e.ChapterId != request.ChapterId))
            return StudyAnswer.Reject(Outcome.Denied);

        // Reject malformed citations and invisible formatting characters in the entire
        // evidence set, rather than returning a valid earlier row and ignoring a later one.
        // The UI treats the citation as plain text; markup is never an allowed citation.
        if (authorizedExcerpts.Any(e => string.IsNullOrWhiteSpace(e.Citation) ||
            e.Citation.Length > MaxCitationLength ||
            e.Citation.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
                char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
                c is '<' or '>' or '&') ||
            string.IsNullOrWhiteSpace(e.Text) ||
            e.Text.Length > MaxExcerptLength ||
            e.Text.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
                char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
                c is '<' or '>')))
            return StudyAnswer.Reject(Outcome.Invalid);

        foreach (var excerpt in authorizedExcerpts)
        {
            if (string.IsNullOrWhiteSpace(excerpt.Text) ||
                string.IsNullOrWhiteSpace(excerpt.Citation) ||
                excerpt.Text.Length > MaxExcerptLength ||
                excerpt.Text.Any(char.IsControl))
                continue;
            var sourceWords = Words.Matches(excerpt.Text.Normalize(NormalizationForm.FormKC)
                .ToLowerInvariant()).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            if (!terms.All(sourceWords.Contains))
                continue;
            // Bounded quotation; never output a whole licensed chapter.
            var sentence = excerpt.Text.Trim();
            if (sentence.Length > 240)
                sentence = sentence[..240].TrimEnd() + "…";
            return new StudyAnswer(Outcome.Answered, sentence, excerpt.Citation);
        }
        return StudyAnswer.Reject(Outcome.NoEvidence);
    }
}
