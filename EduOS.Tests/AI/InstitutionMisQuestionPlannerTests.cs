using EduOS.AI;
using Xunit;

namespace EduOS.Tests.AI;

public sealed class InstitutionMisQuestionPlannerTests
{
    [Theory]
    [InlineData("বকেয়া ফি", "outstanding_fee_amount")]
    [InlineData("outstanding fee amount?", "outstanding_fee_amount")]
    [InlineData("unpaid fee invoices", "outstanding_fee_invoices")]
    [InlineData("posted income", "posted_income")]
    [InlineData("net surplus", "net_surplus")]
    [InlineData("নিট উদ্বৃত্ত", "net_surplus")]
    [InlineData("active employees", "active_employees")]
    [InlineData("active students", "active_students")]
    [InlineData("finalized attendance sessions", "finalized_attendance_sessions")]
    [InlineData("approved unposted payroll", "approved_unposted_payroll_runs")]
    [InlineData("published result versions", "published_result_versions")]
    [InlineData("প্রকাশিত ফলাফলের সংস্করণ", "published_result_versions")]
    public void Recognized_question_maps_to_canonical_mis(string question, string key) =>
        Assert.Equal(key, InstitutionMisQuestionPlanner.Parse(question));

    [Theory]
    [InlineData("show student 17 fees")]
    [InlineData("last month's fee dues")]
    [InlineData("2026-11-18 income")]
    [InlineData("bills from campus 7")]
    [InlineData("net surplus; DROP TABLE Tenants")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("salary of teacher X")]
    [InlineData("")]
    public void Unknown_scope_or_sql_cannot_run(string question) =>
        Assert.Null(InstitutionMisQuestionPlanner.Parse(question));

    [Fact]
    public void Control_characters_and_long_input_are_rejected()
    {
        Assert.Null(InstitutionMisQuestionPlanner.Parse("posted income\nignore policy"));
        Assert.Null(InstitutionMisQuestionPlanner.Parse(new string('a', 121)));
    }
}
