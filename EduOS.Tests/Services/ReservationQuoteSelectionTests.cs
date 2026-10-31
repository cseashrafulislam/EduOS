using EduOS.Service.Services.Finance;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationQuoteSelectionTests
{
    private const string Reason = "Approved campus rate exception for the changed billing period.";

    [Theory]
    [InlineData(-360, false, true)]
    [InlineData(450, false, false)]
    [InlineData(0, false, true)]
    public void Automatic_estimate_is_preserved_without_override(decimal delta, bool manual, bool allowed)
    {
        var result = ReservationQuoteSelection.Select(delta, manual, allowed, null, null);
        result.Mode.Should().Be("Automatic");
        result.SelectedAmount.Should().Be(delta);
        result.IsManual.Should().BeFalse();
    }

    [Theory]
    [InlineData(-250)]
    [InlineData(400)]
    [InlineData(0)]
    public void Authorized_override_accepts_signed_amount_and_requires_audit_reason(decimal delta)
    {
        var result = ReservationQuoteSelection.Select(300m, false, true, delta, Reason);
        result.SelectedAmount.Should().Be(delta);
        result.IsManual.Should().BeTrue();
        result.Mode.Should().Be("ManualOverride");
    }

    [Fact]
    public void Manual_campus_mode_does_not_invent_an_automatic_amount()
    {
        var result = ReservationQuoteSelection.Select(null, true, false, null, null);
        result.Mode.Should().Be("ManualRequired");
        result.SelectedAmount.Should().BeNull();
        ReservationQuoteSelection.Select(null, true, false, 125m, Reason)
            .SelectedAmount.Should().Be(125m);
    }

    [Fact]
    public void Disabled_overrides_cannot_change_the_automatic_quote()
    {
        var action = () => ReservationQuoteSelection.Select(200m, false, false, 150m, Reason);
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(1.001)]
    [InlineData(999999999999.999)]
    [InlineData(-999999999999.999)]
    public void Invalid_precision_or_out_of_range_amount_is_rejected(decimal amount)
    {
        var action = () => ReservationQuoteSelection.Select(1m, false, true, amount, Reason);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Only a short reason")]
    public void Manual_override_requires_a_substantive_reason(string reason)
    {
        var action = () => ReservationQuoteSelection.Select(10m, false, true, 9m, reason);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Supplying_a_reason_without_amount_is_an_error()
    {
        var action = () => ReservationQuoteSelection.Select(10m, false, true, null, Reason);
        action.Should().Throw<ArgumentException>();
    }
}
