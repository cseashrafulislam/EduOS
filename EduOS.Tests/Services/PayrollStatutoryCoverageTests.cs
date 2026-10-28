using EduOS.Service.Services.HR;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollStatutoryCoverageTests
{
    [Fact]
    public void Missing_any_configured_statutory_component_blocks_salary_run()
    {
        PayrollStatutoryCoverage.IncludesEach([11, 12], [1, 2, 11]).Should().BeFalse();
    }

    [Fact]
    public void Every_statutory_component_included_allows_zero_amount_exemptions()
    {
        // Coverage is by component ID; the salary structure independently verifies
        // the effective period and decimal amount, including explicit zero.
        PayrollStatutoryCoverage.IncludesEach([11, 12], [1, 11, 12]).Should().BeTrue();
        PayrollStatutoryCoverage.IncludesEach([], [1, 2]).Should().BeTrue();
    }
}
