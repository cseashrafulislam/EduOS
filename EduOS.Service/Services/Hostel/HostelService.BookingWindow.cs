using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Finite reservations align with the existing 90-day bed-availability planner.</summary>
    public static bool IsValidFiniteBookingPeriod(DateOnly start, DateOnly? end, DateOnly today) =>
        !end.HasValue || (start >= today && start <= today.AddDays(365) &&
            end.Value >= start && end.Value.DayNumber - start.DayNumber <= 89);

    public static bool BookingPeriodsOverlap(DateOnly start, DateOnly? end,
        DateOnly otherStart, DateOnly? otherEnd) =>
        start <= (otherEnd ?? DateOnly.MaxValue) &&
        otherStart <= (end ?? DateOnly.MaxValue);
}
