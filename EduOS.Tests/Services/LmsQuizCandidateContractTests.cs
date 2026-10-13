using EduOS.Core.DTOs.LMS;
using System.Text.Json;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class LmsQuizCandidateContractTests
{
    [Fact]
    public void Candidate_response_never_exposes_answer_key()
    {
        var dto = new QuizCandidateDto
        {
            Reference = Guid.NewGuid(), Title = "Practice",
            Questions = new List<QuizCandidateQuestionDto>
            {
                new() { Id = 1, QuestionText = "Question",
                    Options = new List<QuizCandidateOptionDto>
                    { new() { Id = 2, OptionText = "Option" } } }
            }
        };
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("IsCorrect", json);
        Assert.DoesNotContain("AwardedMarks", json);
    }
}
