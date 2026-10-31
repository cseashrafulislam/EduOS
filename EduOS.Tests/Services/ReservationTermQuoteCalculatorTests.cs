using EduOS.Service.Services.Finance;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationTermQuoteCalculatorTests
{
    [Fact]
    public void Daily_prorated_hostel_start_change_estimates_credit_without_posting()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 30),
            new(2026, 11, 10), new(2026, 11, 30), 1200m, 1);
        result.OriginalAmount.Should().Be(1200m);
        result.ProposedAmount.Should().Be(840m);
        result.NetAdjustment.Should().Be(-360m);
        result.NeedsManualReview.Should().BeFalse();
    }

    [Fact]
    public void Daily_prorated_transport_extension_estimates_positive_charge()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 15),
            new(2026, 11, 1), new(2026, 11, 30), 900m, 1);
        result.OriginalAmount.Should().Be(450m);
        result.ProposedAmount.Should().Be(900m);
        result.NetAdjustment.Should().Be(450m);
    }

    [Fact]
    public void Leap_february_uses_actual_month_length_and_away_from_zero_rounding()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2028, 2, 1), new(2028, 2, 28),
            new(2028, 2, 1), new(2028, 2, 29), 100m, 1);
        result.OriginalAmount.Should().Be(96.55m);
        result.ProposedAmount.Should().Be(100m);
        result.NetAdjustment.Should().Be(3.45m);
    }

    [Fact]
    public void Fixed_monthly_mode_charges_each_touched_month_only_once()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 10),
            new(2026, 11, 1), new(2026, 12, 2), 1000m, 2);
        result.OriginalAmount.Should().Be(1000m);
        result.ProposedAmount.Should().Be(2000m);
        result.NetAdjustment.Should().Be(1000m);
    }

    [Fact]
    public void Fixed_monthly_change_inside_same_month_suggests_zero_without_granting_approval()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 30),
            new(2026, 11, 20), new(2026, 11, 30), 1500m, 2);
        result.NetAdjustment.Should().Be(0m);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public void Manual_and_open_ended_contracts_never_invent_an_amount(int mode, bool openEnded)
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1),
            openEnded ? null : new DateOnly(2026, 11, 30), new(2026, 11, 2),
            openEnded ? null : new DateOnly(2026, 11, 30), 1500m, mode);
        result.NeedsManualReview.Should().BeTrue();
        result.NetAdjustment.Should().BeNull();
    }

    [Fact]
    public void Zero_charge_cannot_become_a_cash_payout()
    {
        var result = ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 30),
            new(2026, 11, 2), new(2026, 12, 3), 0m, 1);
        result.NetAdjustment.Should().Be(0m);
    }

    [Fact]
    public void Invalid_period_or_billing_mode_is_rejected()
    {
        var run = () => ReservationTermQuoteCalculator.Calculate(new(2026, 11, 30), new(2026, 11, 1),
            new(2026, 11, 1), new(2026, 11, 30), 100m, 1);
        run.Should().Throw<ArgumentOutOfRangeException>();
        var unknown = () => ReservationTermQuoteCalculator.Calculate(new(2026, 11, 1), new(2026, 11, 30),
            new(2026, 11, 1), new(2026, 11, 30), 100m, 9);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }
}
