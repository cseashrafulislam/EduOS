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
            // The displayed quotation must itself contain every matched question
            // term. A match hidden beyond the first 240 characters is not evidence
            // for the visible answer. Never expose an unrelated prefix as a citation.
            const int maxQuoteLength = 240;
            var passage = excerpt.Text.Trim();
            if (passage.Length <= maxQuoteLength)
                return new StudyAnswer(Outcome.Answered, passage, excerpt.Citation);

            // A sliding substring can split a source token at either edge and
            // incorrectly treat that fragment as a complete evidence word.
            // Match complete source tokens first, then select a bounded window
            // containing the full tokens. Offsets stay in the original string.
            var matches = Words.Matches(passage).Cast<Match>().ToArray();
            for (var first = 0; first < matches.Length; first++)
            {
                var start = matches[first].Index;
                var endLimit = Math.Min(passage.Length, start + maxQuoteLength);
                var visible = matches.Where(m => m.Index >= start &&
                    m.Index + m.Length <= endLimit).ToArray();
                var visibleWords = visible.Select(m => m.Value.Normalize(NormalizationForm.FormKC)
                    .ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
                if (!terms.All(visibleWords.Contains))
                    continue;
                // Do not truncate the final word; the quote must be composed of
                // whole source tokens and cannot claim a partial word as evidence.
                var last = visible[^1];
                var end = last.Index + last.Length;
                var window = passage.Substring(start, end - start);
                var quote = (start > 0 ? "…" : "") + window.Trim() +
                    (end < passage.Length ? "…" : "");
                return new StudyAnswer(Outcome.Answered, quote, excerpt.Citation);
            }
            // Terms scattered across the chapter do not support one bounded quote.
            continue;
        }
        return StudyAnswer.Reject(Outcome.NoEvidence);
    }
}
