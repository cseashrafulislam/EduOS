using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.LMS;
public sealed partial class LmsAssessmentService
{
    private static Dictionary<long, long[]>? ValidateSelections(
        IReadOnlyList<SubmitQuizAnswerRequestDto> answers, List<QuizQuestion> questions, List<QuizOption> options)
    {
        if (answers.GroupBy(x => x.QuizQuestionId).Any(g => g.Count() != 1)) return null;
        var selected = new Dictionary<long, long[]>();
        foreach (var input in answers)
        {
            var question = questions.FirstOrDefault(x => x.Id == input.QuizQuestionId);
            if (question == null || input.AnswerText != null ||
                (question.QuestionType != "SingleChoice" && question.QuestionType != "MultipleChoice"))
                return null;
            var ids = input.SelectedOptionIds?.ToArray() ?? Array.Empty<long>();
            if (input.SelectedOptionId.HasValue)
            {
                if (ids.Length != 0) return null;
                ids = [input.SelectedOptionId.Value];
            }
            if (ids.Length > 10 || (question.QuestionType == "SingleChoice" && ids.Length > 1) ||
                ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Length ||
                ids.Any(x => !options.Any(o => o.Id == x && o.QuizQuestionId == question.Id)))
                return null;
            selected.Add(question.Id, ids);
        }
        return selected;
    }

    private async Task<bool> SameSubmittedAnswersAsync(long attemptId,
        Dictionary<long, long[]> selected, CancellationToken ct)
    {
        var stored = await _answers.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.QuizAttemptId == attemptId && !x.IsDeleted).ToListAsync(ct);
        var ids = stored.Select(x => x.Id).ToArray();
        var links = await _answerOptions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.QuizAnswerId) && !x.IsDeleted).ToListAsync(ct);
        if (stored.Count != selected.Count) return false;
        foreach (var row in stored)
        {
            var saved = row.SelectedOptionId.HasValue ? new[] { row.SelectedOptionId.Value } :
                links.Where(x => x.QuizAnswerId == row.Id).Select(x => x.QuizOptionId).ToArray();
            if (!selected.TryGetValue(row.QuizQuestionId, out var received) ||
                !saved.OrderBy(x => x).SequenceEqual(received.OrderBy(x => x))) return false;
        }
        return true;
    }

    private async Task<decimal> PersistSelectedAnswersAsync(long attemptId,
        List<QuizQuestion> questions, List<QuizOption> options, Dictionary<long, long[]> selections,
        CancellationToken ct)
    {
        decimal total = 0;
        var multi = new List<(QuizAnswer Row, long[] Ids)>();
        foreach (var entry in selections)
        {
            var question = questions.First(x => x.Id == entry.Key);
            var correct = options.Where(o => o.QuizQuestionId == question.Id && o.IsCorrect)
                .Select(o => o.Id).OrderBy(x => x).ToArray();
            // Exact-set scoring: wrong, missing, extra, and empty selections earn zero.
            var marks = entry.Value.Length > 0 &&
                entry.Value.OrderBy(x => x).SequenceEqual(correct) ? question.Marks : 0m;
            total += marks;
            var row = new QuizAnswer { TenantId = _user.TenantId, QuizAttemptId = attemptId,
                QuizQuestionId = question.Id, AwardedMarks = marks,
                SelectedOptionId = question.QuestionType == "SingleChoice" && entry.Value.Length == 1
                    ? entry.Value[0] : null, CreatedAt = Now, CreatedBy = _user.UserId };
            await _answers.AddAsync(row);
            if (question.QuestionType == "MultipleChoice") multi.Add((row, entry.Value));
        }
        await _uow.SaveChangesAsync(ct);
        foreach (var item in multi)
            foreach (var optionId in item.Ids)
                await _answerOptions.AddAsync(new QuizAnswerOption { TenantId = _user.TenantId,
                    QuizAnswerId = item.Row.Id, QuizOptionId = optionId,
                    CreatedAt = Now, CreatedBy = _user.UserId });
        return total;
    }
}
