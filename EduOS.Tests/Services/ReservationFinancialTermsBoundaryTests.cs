using EduOS.Service.Services.Finance;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationFinancialTermsBoundaryTests
{
    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(1, 0, false, true)]
    [InlineData(-1, 0, false, true)]
    [InlineData(0, 0.01, false, true)]
    [InlineData(0, 0, true, true)]
    public void Finance_requires_approval_for_any_chargeable_reservation_term(
        decimal monthlyCharge, decimal securityDeposit, bool feeHead, bool expected)
    {
        Assert.Equal(expected, ReservationFinancePolicy.HasChargeableTerms(
            monthlyCharge, securityDeposit, feeHead));
    }

    [Theory]
    [InlineData("Hostel", "HostelService.AllocationCancellation.cs")]
    [InlineData("Hostel", "HostelService.AllocationClosure.cs")]
    [InlineData("Transport", "TransportService.UpcomingStartReschedule.cs")]
    [InlineData("Transport", "TransportService.RiderExtension.cs")]
    [InlineData("Transport", "TransportService.UpcomingCancellation.cs")]
    [InlineData("Transport", "TransportService.cs")]
    public void Reservation_commands_recheck_financial_terms_before_mutating(
        string module, string filename)
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", module, filename));
        if (filename == "TransportService.cs")
        {
            var start = source.IndexOf("Task<ApiResponse<StudentTransportDto>> CloseAsync", StringComparison.Ordinal);
            var end = source.IndexOf("public static bool IsValidTransportClosureDate", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            source = source[start..end];
        }
        var postedGate = source.IndexOf("_reservationFinancePolicy.HasPostedActivityAsync(", StringComparison.Ordinal);
        var chargeGate = source.IndexOf("ReservationFinancePolicy.HasChargeableTerms(", StringComparison.Ordinal);
        var mutation = source.IndexOf(".UpdatedAt = _clock.GetUtcNow()", StringComparison.Ordinal);
        Assert.True(postedGate >= 0, "Missing posted/issued financial exposure gate.");
        Assert.True(chargeGate > postedGate, "Chargeable terms must be checked after the posting gate.");
        Assert.True(mutation > chargeGate, "The financial gate must precede any reservation mutation.");
        Assert.Contains("System.Data.IsolationLevel.Serializable", source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
