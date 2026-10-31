using EduOS.Core.DTOs.Finance;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Finance;
using EduOS.Service.Services.Finance;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationPeriodSettlementTests
{
    [Theory]
    [InlineData("150.01", true)]
    [InlineData("-150.01", true)]
    [InlineData("0", true)]
    [InlineData("150.001", false)]
    [InlineData("9999999999999", false)]
    [InlineData("-9999999999999", false)]
    public void Signed_money_is_bounded_and_has_two_decimal_places(string value, bool valid) =>
        Assert.Equal(valid, ReservationPeriodSettlement.ValidAmount(decimal.Parse(value,
            System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_positive_charge_must_debit_exact_student_receivable()
    {
        var correct = new (long?, decimal, decimal)[] { (12, 150m, 0m), (null, 0m, 150m) };
        Assert.True(ReservationPeriodSettlement.MatchesSignedStudentLeg(150m, correct, 12));
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(-150m, correct, 12));
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(149m, correct, 12));
    }

    [Fact]
    public void A_negative_credit_must_credit_exact_student_receivable()
    {
        var correct = new (long?, decimal, decimal)[] { (null, 100m, 0m), (12, 0m, 100m) };
        Assert.True(ReservationPeriodSettlement.MatchesSignedStudentLeg(-100m, correct, 12));
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(100m, correct, 12));
    }

    [Fact]
    public void Mixed_students_or_unbalanced_lines_cannot_back_a_clearance()
    {
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(100m,
            new (long?, decimal, decimal)[] { (12, 100m, 0m), (13, 0m, 100m) }, 12));
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(100m,
            new (long?, decimal, decimal)[] { (12, 100m, 0m), (null, 0m, 90m) }, 12));
        Assert.False(ReservationPeriodSettlement.MatchesSignedStudentLeg(0m,
            new (long?, decimal, decimal)[] { (12, 0m, 0m), (null, 0m, 0m) }, 12));
    }

    [Fact]
    public void Versioned_operations_have_distinct_non_reused_values()
    {
        Assert.Equal(3, (int)HostelClearanceOperation.UpcomingStartReschedule);
        Assert.Equal(3, (int)TransportClearanceOperation.UpcomingRiderExtension);
        Assert.Equal(2, (int)HostelClearanceOperation.Checkout);
        Assert.Equal(2, (int)TransportClearanceOperation.Closure);
    }

    [Fact]
    public void Signed_money_can_be_transferred_as_decimal_text_without_floating_point_loss()
    {
        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var hostel = JsonSerializer.Deserialize<RequestHostelClearanceDto>("""
            {"manualNetAdjustment":"-999999999999.99"}
            """, webOptions);
        var transport = JsonSerializer.Deserialize<RequestTransportClearanceDto>("""
            {"manualNetAdjustment":"999999999999.99"}
            """, webOptions);
        var review = JsonSerializer.Deserialize<ReviewTransportClearanceDto>("""
            {"manualNetAdjustment":"-0.01"}
            """, webOptions);
        Assert.Equal(-999999999999.99m, hostel!.ManualNetAdjustment);
        Assert.Equal(999999999999.99m, transport!.ManualNetAdjustment);
        Assert.Equal(-0.01m, review!.ManualNetAdjustment);
    }

    [Fact]
    public void Existing_operations_have_no_implicit_period_mutation_fields()
    {
        Assert.Null(new RescheduleUpcomingHostelStartRequestDto().FinanceClearanceId);
        Assert.Null(new ExtendUpcomingTransportAssignmentRequestDto().FinanceClearanceId);
    }
}
