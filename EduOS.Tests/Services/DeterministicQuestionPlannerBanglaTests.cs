using EduOS.AI;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DeterministicQuestionPlannerBanglaTests
{
    private static readonly DateOnly LocalToday = new(2026, 11, 7);

    [Theory]
    [InlineData("আজকে উপস্থিতি কতজন উপস্থিত?", "2026-11-07", "present")]
    [InlineData("আজকের হাজিরা অনুপস্থিত কয়জন?", "2026-11-07", "absent")]
    [InlineData("গতকালকে হাজিরা অনুপস্থিত কয়জন?", "2026-11-06", "absent")]
    [InlineData("গতকালের attendance present count?", "2026-11-06", "present")]
    [InlineData("aajke attendance absent koto?", "2026-11-07", "absent")]
    [InlineData("gotokalke attendance present koto?", "2026-11-06", "present")]
    public void Common_bangladesh_date_and_count_variants_resolve_to_reviewed_slots(
        string question, string expectedDate, string expectedStatus)
    {
        var plan = DeterministicQuestionPlanner.Parse(question, LocalToday);
        plan.Status.Should().Be(PlanStatus.Ready);
        plan.RegisteredMetricKey.Should().Be("attendance.status.count.v1");
        plan.Slots["date"].Should().Be(expectedDate);
        plan.Slots["status"].Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("আজকে গতকাল উপস্থিতি কতজন উপস্থিত?")]
    [InlineData("আজকে উপস্থিতি 2026-11-07 কতজন উপস্থিত?")]
    [InlineData("আজকে উপস্থিতি কতজন উপস্থিত; DROP TABLE Students")]
    [InlineData("আজকে উপস্থিতি কতজন উপস্থিত -- extra")]
    [InlineData("আজকে উপস্থিতি কতজন উপস্থিত student 42")]
    [InlineData("আজকে হাজিরা কতজন উপস্থিত অনুপস্থিত?")]
    [InlineData("আজকে উপস্থিতি কতজন উপস্থিত 2026-11-07 2026-11-06?")]
    public void Ambiguous_or_unreviewed_questions_fail_closed(string question)
    {
        var plan = DeterministicQuestionPlanner.Parse(question, LocalToday);
        plan.Status.Should().NotBe(PlanStatus.Ready);
        plan.RegisteredMetricKey.Should().BeNull();
        plan.Slots.Should().BeEmpty();
    }

    [Fact]
    public void Authorized_capability_planning_rejects_missing_permission_even_for_valid_bangla()
    {
        var planner = new LocalFirstCapabilityPlanner(ReviewedCapabilityRegistry.AttendanceOnly());
        var restricted = new AiRequestScope("101", "7", "42",
            new HashSet<string>(StringComparer.Ordinal), true);

        var result = planner.PlanAttendance("আজকে হাজিরা কতজন উপস্থিত?", LocalToday, restricted);
        result.Decision.Should().Be(CapabilityDecision.Denied);
        result.Query.Should().BeNull();
    }
}
