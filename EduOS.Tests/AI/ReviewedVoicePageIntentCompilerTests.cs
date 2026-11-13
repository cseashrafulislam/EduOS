using EduOS.AI;
using Xunit;

namespace EduOS.Tests.AI;

public sealed class ReviewedVoicePageIntentCompilerTests
{
    private static VoicePagePreviewRequest Request(string command) => new()
    {
        PagePath = "/Admissions/NewApplication",
        Utterance = command,
        Targets =
        [
            new VoicePageTarget { Key = "publicNote", Kind = "text",
                Labels = [ "Public note", "সাধারণ মন্তব্য" ] },
            new VoicePageTarget { Key = "searchTerm", Kind = "filter",
                Labels = [ "Student search", "শিক্ষার্থী খুঁজুন" ] },
            new VoicePageTarget { Key = "classChoice", Kind = "select",
                Labels = [ "Class", "শ্রেণি" ],
                Options =
                [
                    new VoicePageOption { Key = "class-one", Labels = [ "One", "এক" ] },
                    new VoicePageOption { Key = "class-two", Labels = [ "Two", "দুই" ] }
                ] },
            new VoicePageTarget { Key = "readOnlyInfo", Kind = "readonly",
                Labels = [ "Information" ] }
        ]
    };

    [Theory]
    [InlineData("fill Public note with Materials ready", "DraftText", "publicNote", "Materials ready")]
    [InlineData("সাধারণ মন্তব্য লিখুন: পাঠ প্রস্তুত", "DraftText", "publicNote", "পাঠ প্রস্তুত")]
    [InlineData("filter Student search for Rahman", "SetFilter", "searchTerm", "Rahman")]
    [InlineData("Student search খুঁজুন: Rahman", "SetFilter", "searchTerm", "Rahman")]
    [InlineData("show Public note field", "FocusField", "publicNote", null)]
    [InlineData("শ্রেণি ঘর দেখাও", "FocusField", "classChoice", null)]
    public void Reviewed_bilingual_commands_return_only_a_preview(
        string command, string action, string key, string? value)
    {
        var plan = ReviewedVoicePageIntentCompiler.Preview(Request(command));
        Assert.Equal(VoicePageDecision.Ready, plan.Decision);
        Assert.Equal(action, plan.Action);
        Assert.Equal(key, plan.TargetKey);
        Assert.Equal(value, plan.ProposedValue);
        Assert.True(plan.RequiresConfirmation);
        Assert.False(plan.BusinessWriteExecuted);
    }

    [Theory]
    [InlineData("select Class as One", "class-one")]
    [InlineData("শ্রেণি নির্বাচন করুন: দুই", "class-two")]
    public void Dropdown_choice_is_resolved_against_supplied_options_only(
        string command, string expectedOption)
    {
        var plan = ReviewedVoicePageIntentCompiler.Preview(Request(command));
        Assert.Equal(VoicePageDecision.Ready, plan.Decision);
        Assert.Equal("SelectOption", plan.Action);
        Assert.Equal(expectedOption, plan.OptionKey);
        Assert.True(plan.RequiresConfirmation);
    }

    [Fact]
    public void Missing_and_ambiguous_options_fail_closed()
    {
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(Request("select Class as Eleven")).Decision);
        var request = Request("select Class as One");
        request.Targets[2].Options.Add(new VoicePageOption
            { Key = "class-one-other", Labels = [ "One" ] });
        Assert.Equal(VoicePageDecision.ClarificationRequired,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
    }

    [Fact]
    public void Duplicated_labels_across_fields_must_be_disambiguated()
    {
        var request = Request("fill Public note with Hello");
        request.Targets[3].Labels.Add("Public note");
        Assert.Equal(VoicePageDecision.ClarificationRequired,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
    }

    [Theory]
    [InlineData("approve payment")]
    [InlineData("delete student")]
    [InlineData("post payroll")]
    [InlineData("submit attendance")]
    [InlineData("fill Public note with <script>")]
    [InlineData("fill Public note with a\nb")]
    public void Protected_commands_and_markup_are_not_executable(string command)
    {
        var plan = ReviewedVoicePageIntentCompiler.Preview(Request(command));
        Assert.Equal(VoicePageDecision.Unavailable, plan.Decision);
        Assert.False(plan.RequiresConfirmation);
        Assert.False(plan.BusinessWriteExecuted);
    }

    [Fact]
    public void Deny_protected_label_even_when_client_calls_it_text()
    {
        var request = Request("fill Public note with abc");
        request.Targets[0].Labels.Add("ব্যাংক হিসাব");
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
        request = Request("fill Public note with abc");
        request.Targets[0].Key = "EmployeeSalary";
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
    }

    [Fact]
    public void Malformed_route_keys_limits_and_metadata_fail_closed()
    {
        var request = Request("show Public note field");
        request.PagePath = "https://example.com/Admin";
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
        request = Request("show Public note field");
        request.Targets[0].Key = "../other";
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
        request = Request("show Public note field");
        request.Targets[3].Key = request.Targets[0].Key;
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
        request = Request("show Public note field");
        request.Targets.Clear();
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
        request = Request("show Public note field");
        request.Utterance = new string('x', 513);
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(request).Decision);
    }

    [Fact]
    public void Readonly_and_ineligible_action_pairs_cannot_write()
    {
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(Request(
                "fill Information with Wrong")).Decision);
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(Request(
                "select Public note as One")).Decision);
        Assert.Equal(VoicePageDecision.Unavailable,
            ReviewedVoicePageIntentCompiler.Preview(Request(
                "filter Class for One")).Decision);
    }
}
