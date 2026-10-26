using System.Text.Json;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using Xunit;

namespace EduOS.Tests.App;

public sealed class LmsQuizAnswerContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Quiz_answer_request_has_one_selection_contract_for_both_question_types()
    {
        var request = new SubmitQuizAnswerRequestDto { QuizQuestionId = 5, SelectedOptionIds = [12] };
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(12L, payload.RootElement.GetProperty("selectedOptionIds")[0].GetInt64());
        Assert.False(payload.RootElement.TryGetProperty("selectedOptionId", out _));
        Assert.Null(typeof(SubmitQuizAnswerRequestDto).GetProperty("SelectedOptionId"));
        // Existing single-choice evidence remains stored in its canonical entity column.
        Assert.NotNull(typeof(QuizAnswer).GetProperty("SelectedOptionId"));
    }

    [Fact]
    public void Quiz_page_uses_only_modular_learner_script_and_canonical_selection_payload()
    {
        var view = File.ReadAllText(Asset("Quizzes.cshtml"));
        var learner = File.ReadAllText(Asset("quizzes-learner.js"));
        Assert.Contains("~/js/learning/quizzes-learner.js", view);
        Assert.DoesNotContain("~/js/learning/quizzes.js", view);
        Assert.Contains("selectedOptionIds:selected", learner);
        Assert.DoesNotContain("selectedOptionId:", learner);
    }
}
