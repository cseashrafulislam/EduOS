using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>
/// Resumable, bounded schedule using each day's existing serializable PlanAsync.
/// Earlier days remain committed when a later date conflicts; replay uses the same
/// deterministic per-date key and cannot create duplicate vehicle slots.
/// </summary>
public sealed partial class TransportTripService
{
    public async Task<ApiResponse<TransportTripScheduleResultDto>> ScheduleAsync(
        ScheduleTransportTripsRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage)
            return ApiResponse<TransportTripScheduleResultDto>.ErrorResponse("Transport permission required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.RouteReference == Guid.Empty || request.VehicleReference == Guid.Empty ||
            request.Direction is not ("Pickup" or "Drop") ||
            request.StartDate < today || request.EndDate < request.StartDate ||
            request.EndDate > today.AddDays(30) ||
            request.EndDate.DayNumber - request.StartDate.DayNumber >= 7)
            return ApiResponse<TransportTripScheduleResultDto>.ErrorResponse(
                "Select a valid route, vehicle, direction and 1–7 dates within the next 30 days.");

        var selected = Enumerable.Range(0, request.EndDate.DayNumber - request.StartDate.DayNumber + 1)
            .Select(offset => request.StartDate.AddDays(offset)).ToArray();
        var calendarClosed = new HashSet<DateOnly>();
        var existingCalendarKeys = new HashSet<Guid>();
        if (request.UseAcademicCalendar)
        {
            // Explicit opt-in: do not infer school holidays for special transport services.
            // The calendar read is bounded and tenant/campus authorized; PlanAsync still
            // independently revalidates each date inside its serializable transaction.
            var calendar = await GetScheduleCalendarAsync(
                request.RouteReference, request.StartDate, request.EndDate, ct);
            if (!calendar.Success || calendar.Data == null)
                return ApiResponse<TransportTripScheduleResultDto>.ErrorResponse(
                    calendar.Message, calendar.StatusCode);
            calendarClosed = calendar.Data.Days.Where(x => !x.IsOperatingDay)
                .Select(x => x.Date).ToHashSet();
            if (calendarClosed.Count > 0)
            {
                // A calendar edit must not erase or hide an earlier committed batch
                // date. Replaying its deterministic key returns the historical plan.
                var closedKeys = calendarClosed
                    .Select(date => ScheduleDateKey(request.ClientRequestId, date)).ToArray();
                existingCalendarKeys = (await _db.TransportTrips.AsNoTracking()
                    .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                        closedKeys.Contains(x.ClientRequestId))
                    .Select(x => x.ClientRequestId).ToListAsync(ct)).ToHashSet();
            }
        }
        var dates = selected.Where(date =>
            (!request.SkipFridaySaturday ||
                date.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday)) &&
            (!calendarClosed.Contains(date) ||
                existingCalendarKeys.Contains(ScheduleDateKey(request.ClientRequestId, date))))
            .ToArray();
        var skipped = selected.Except(dates).ToArray();
        if (dates.Length == 0)
        {
            if (!request.UseAcademicCalendar)
                return ApiResponse<TransportTripScheduleResultDto>.ErrorResponse(
                    "No selected operating days.");
            return ApiResponse<TransportTripScheduleResultDto>.SuccessResponse(
                new TransportTripScheduleResultDto
                {
                    SelectedDays = 0, SkippedDates = skipped
                }, "No open service dates; no trips created.");
        }

        var rows = new List<TransportTripRowDto>(dates.Length);
        var created = 0;
        var replayed = 0;
        foreach (var date in dates)
        {
            ct.ThrowIfCancellationRequested();
            var response = await PlanAsync(new PlanTransportTripRequestDto
            {
                ClientRequestId = ScheduleDateKey(request.ClientRequestId, date),
                RouteReference = request.RouteReference,
                VehicleReference = request.VehicleReference,
                OperatingDate = date,
                Direction = request.Direction
            }, ct);
            if (!response.Success || response.Data == null)
                return new ApiResponse<TransportTripScheduleResultDto>
                {
                    Success = true, StatusCode = 207,
                    Message = "Scheduling stopped at a conflicting date; earlier dates remain committed.",
                    Data = new TransportTripScheduleResultDto
                    {
                        SelectedDays = dates.Length, NewlyPlanned = created,
                        AlreadyPlanned = replayed, SkippedDates = skipped, Trips = rows,
                        FailedDate = date, FailureStatusCode = response.StatusCode,
                        FailureMessage = response.Message
                    }
                };
            rows.Add(response.Data);
            if (response.StatusCode == 201) created++;
            else replayed++;
        }
        return ApiResponse<TransportTripScheduleResultDto>.SuccessResponse(
            new TransportTripScheduleResultDto
            {
                SelectedDays = dates.Length, NewlyPlanned = created,
                AlreadyPlanned = replayed, SkippedDates = skipped, Trips = rows
            }, "Selected dates planned or safely replayed.");
    }

    private static Guid ScheduleDateKey(Guid batchKey, DateOnly date)
    {
        // Route, vehicle and direction deliberately excluded: PlanAsync rejects
        // changed payload for the same per-date key rather than silently accepting it.
        var material = Encoding.UTF8.GetBytes(
            "EduOS.Transport.Schedule.v1:" + batchKey.ToString("N") + ":" +
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return new Guid(SHA256.HashData(material).AsSpan(0, 16));
    }
}
