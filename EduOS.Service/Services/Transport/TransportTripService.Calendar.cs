using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

/// <summary>
/// Opt-in academic calendar readback for campus-scoped transport scheduling.
/// Returns closure classifications only: private academic event titles are never exposed.
/// Manual trip planning remains available for approved special-event services.
/// </summary>
public sealed partial class TransportTripService
{
    public async Task<ApiResponse<TransportTripCalendarDto>> GetScheduleCalendarAsync(
        Guid routeReference, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (!CanManage)
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Transport permission required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (routeReference == Guid.Empty || startDate < today ||
            endDate < startDate || endDate > today.AddDays(30) ||
            endDate.DayNumber - startDate.DayNumber >= 7)
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Select a valid route and 1–7 dates within the next 30 days.");

        var tenant = _user.TenantId;
        var route = await _db.Routes.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                x.PublicId == routeReference && x.CampusId.HasValue)
            .Select(x => new { CampusId = x.CampusId!.Value })
            .FirstOrDefaultAsync(ct);
        if (route == null)
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Active campus route not found.", 404);
        if (!await HasCampusAsync(route.CampusId, ct))
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Campus access required.", 403);

        // The campus-specific policy wins over the tenant-wide fallback,
        // matching the existing V1 academic calendar ownership contract.
        var policy = await _db.AcademicCalendarPolicies.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                (!x.CampusId.HasValue || x.CampusId == route.CampusId))
            .OrderByDescending(x => x.CampusId == route.CampusId)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.WeekendDay1, x.WeekendDay2 })
            .FirstOrDefaultAsync(ct);
        if (policy == null)
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Configure an academic calendar policy before using calendar-aware scheduling.", 409);

        var days = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset => startDate.AddDays(offset)).ToArray();
        var years = await _db.AcademicYears.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                (!x.CampusId.HasValue || x.CampusId == route.CampusId) &&
                x.StartDate <= endDate && x.EndDate >= startDate)
            .Select(x => new { x.Id, x.StartDate, x.EndDate })
            .Take(17).ToListAsync(ct);
        if (years.Count == 0 || years.Count > 16 ||
            days.Any(date => !years.Any(year =>
                year.StartDate <= date && year.EndDate >= date)))
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "An active academic year must cover every selected service date.", 409);

        var yearIds = years.Select(x => x.Id).ToArray();
        var holidays = await _db.AcademicCalendarEvents.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsHoliday &&
                yearIds.Contains(x.AcademicYearId) &&
                (!x.CampusId.HasValue || x.CampusId == route.CampusId) &&
                x.StartDate <= endDate && x.EndDate >= startDate)
            .Select(x => new { x.StartDate, x.EndDate })
            .Take(101).ToListAsync(ct);
        if (holidays.Count > 100)
            return ApiResponse<TransportTripCalendarDto>.ErrorResponse(
                "Too many calendar closures; narrow the date range.", 409);

        var rows = days.Select(date =>
        {
            var weekend = date.DayOfWeek == policy.WeekendDay1 ||
                (policy.WeekendDay2.HasValue && date.DayOfWeek == policy.WeekendDay2.Value);
            var holiday = holidays.Any(x => x.StartDate <= date && x.EndDate >= date);
            return new TransportTripCalendarDayDto
            {
                Date = date,
                IsOperatingDay = !weekend && !holiday,
                ClosureReason = holiday ? "Holiday" : weekend ? "Weekend" : null
            };
        }).ToArray();
        return ApiResponse<TransportTripCalendarDto>.SuccessResponse(
            new TransportTripCalendarDto { CampusId = route.CampusId, Days = rows });
    }
}
