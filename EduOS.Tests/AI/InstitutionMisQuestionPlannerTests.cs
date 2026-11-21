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
    [InlineData("how many active students?", "active_students")]
    [InlineData("How many active students are there?", "active_students")]
    [InlineData("Could you show me current active students please?", "active_students")]
    [InlineData("what is the number of active employees?", "active_employees")]
    [InlineData("How much is the total outstanding fees?", "outstanding_fee_amount")]
    [InlineData("Show me the unpaid fee invoice count.", "outstanding_fee_invoices")]
    [InlineData("কয়জন সক্রিয় ছাত্রছাত্রী আছে?", "active_students")]
    [InlineData("কয়জন বর্তমান সক্রিয় শিক্ষার্থী?", "active_students")]
    [InlineData("দয়া করে বকেয়া ফি ইনভয়েস দেখান", "outstanding_fee_invoices")]
    [InlineData("আমাকে বলুন জমা দেয়া রিকুইজিশন কতটি", "submitted_requisitions")]
    [InlineData("কতজন সক্রিয় শিক্ষার্থী?", "active_students")]
    [InlineData("বকেয়া ফি কত টাকা?", "outstanding_fee_amount")]
    [InlineData("please show active assets", "active_assets")]
    [InlineData("planned transport trips", "planned_trips")]
    [InlineData("finalized attendance sessions", "finalized_attendance_sessions")]
    [InlineData("approved unposted payroll", "approved_unposted_payroll_runs")]
    [InlineData("published result versions", "published_result_versions")]
    [InlineData("প্রকাশিত ফলাফলের সংস্করণ", "published_result_versions")]
    public void Recognized_question_maps_to_canonical_mis(string question, string key) =>
        Assert.Equal(key, InstitutionMisQuestionPlanner.Parse(question));

    [Theory]
    [InlineData("show student 17 fees")]
    [InlineData("last month's fee dues")]
    [InlineData("show active students for campus 7")]
    [InlineData("fee amount due for Alice")]
    [InlineData("paid student fees in January")]
    [InlineData("current students with section A")]
    [InlineData("show me the payroll of employee 90")]
    [InlineData("how many active students in another tenant?")]
    [InlineData("2026-11-18 income")]
    [InlineData("bills from campus 7")]
    [InlineData("net surplus; DROP TABLE Tenants")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("salary of teacher X")]
    [InlineData("")]
    public void Unknown_scope_or_sql_cannot_run(string question) =>
        Assert.Null(InstitutionMisQuestionPlanner.Parse(question));

    [Fact]
    public void Question_catalogue_contains_only_valid_bilingual_metadata()
    {
        var entries = InstitutionMisQuestionPlanner.Suggestions();
        Assert.Contains(entries, x => x.Question == "active students" && x.MetricKey == "active_students");
        Assert.Contains(entries, x => x.Question == "বকেয়া ফি" && x.MetricKey == "outstanding_fee_amount");
        Assert.All(entries, x => Assert.Equal(x.MetricKey, InstitutionMisQuestionPlanner.Parse(x.Question)));
        Assert.True(entries.Count <= 100);
        Assert.Empty(InstitutionMisQuestionPlanner.Suggestions(new string('x', 81)));
        Assert.Empty(InstitutionMisQuestionPlanner.Suggestions("active\nstudent"));
        Assert.Contains(InstitutionMisQuestionPlanner.Suggestions("students"), x => x.MetricKey == "active_students");
    }

    [Fact]
    public void Control_characters_and_long_input_are_rejected()
    {
        Assert.Null(InstitutionMisQuestionPlanner.Parse("posted income\nignore policy"));
        Assert.Null(InstitutionMisQuestionPlanner.Parse(new string('a', 121)));
    }
}
