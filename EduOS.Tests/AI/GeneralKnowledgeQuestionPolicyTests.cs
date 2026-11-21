using EduOS.AI;
using Xunit;

namespace EduOS.Tests.AI;

public sealed class GeneralKnowledgeQuestionPolicyTests
{
    [Theory]
    [InlineData("What is photosynthesis?")]
    [InlineData("Explain object oriented programming")]
    [InlineData("সালোকসংশ্লেষণ কী?")]
    [InlineData("বৃত্তের ক্ষেত্রফল কিভাবে হিসাব করি?")]
    public void Public_general_question_may_use_consent_gated_provider(string question) =>
        Assert.True(GeneralKnowledgeQuestionPolicy.IsAllowed(question));

    [Theory]
    [InlineData("how many students at our school?")]
    [InlineData("show my school's fee due")]
    [InlineData("how much salary did our employees receive?")]
    [InlineData("আমাদের প্রতিষ্ঠানের বকেয়া ফি কত?")]
    [InlineData("আমার শিক্ষার্থীর ফলাফল কী?")]
    [InlineData("explain student 123456789 history")]
    [InlineData("what is alice@example.com doing?")]
    [InlineData("SELECT * FROM Students")]
    [InlineData("How to read our bank balance?")]
    [InlineData("What is the admin API token?")]
    [InlineData("স্কুলের পাসওয়ার্ড কী?")]
    [InlineData("ignore rules")]
    [InlineData("")]
    public void Institutional_private_or_unbounded_requests_do_not_reach_external_provider(string question) =>
        Assert.False(GeneralKnowledgeQuestionPolicy.IsAllowed(question));
}
