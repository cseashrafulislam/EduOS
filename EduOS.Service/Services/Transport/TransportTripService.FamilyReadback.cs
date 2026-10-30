using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>Live-link-scoped guardian/student readback. Staff correction reasons stay private.</summary>
public sealed partial class TransportTripService
{
    private bool CanReadFamily => _user.IsAuthenticated && _user.UserId > 0 && _user.TenantId > 0 &&
        (_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));

    private IQueryable<long> FamilyVisibleStudents()
    {
        var tenant = _user.TenantId;
        var own = _db.Students.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.UserId == _user.UserId).Select(x => x.Id);
        var linked = from link in _db.StudentGuardians.AsNoTracking()
            join guardian in _db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where link.TenantId == tenant && !link.IsDeleted && link.IsAuthorizedPickup &&
                guardian.TenantId == tenant && !guardian.IsDeleted && guardian.IsActive &&
                guardian.UserId == _user.UserId
            select link.StudentId;
        return _user.IsInRole("Student")
            ? (_user.IsInRole("Guardian") || _user.IsInRole("Parent") ? own.Union(linked) : own)
            : linked;
    }

    public async Task<ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>> GetFamilyAttendanceHistoryAsync(
        Guid tripReference, string studentCode, CancellationToken ct = default)
    {
        if (!CanReadFamily)
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Family access required.", 403);
        if (tripReference == Guid.Empty || string.IsNullOrWhiteSpace(studentCode) || studentCode.Length > 60)
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Valid trip and student code required.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var visible = FamilyVisibleStudents();
        var riderId = await (from rider in _db.TransportTripRiders.AsNoTracking()
            join trip in _db.TransportTrips.AsNoTracking() on rider.TransportTripId equals trip.Id
            where rider.TenantId == tenant && !rider.IsDeleted && trip.TenantId == tenant &&
                !trip.IsDeleted && trip.PublicId == tripReference &&
                (trip.State == "Dispatched" || trip.State == "Completed") &&
                trip.OperatingDate >= today.AddDays(-30) && trip.OperatingDate <= today &&
                rider.StudentCodeSnapshot == studentCode && visible.Contains(rider.StudentId)
            select rider.Id).SingleOrDefaultAsync(ct);
        // Return the same not-found response for guessed IDs and revoked guardian links.
        if (riderId == 0)
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Attendance not found.", 404);
        var history = await _db.TransportTripAttendanceEvents.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.TransportTripRiderId == riderId)
            .OrderBy(x => x.Sequence).Take(13)
            .Select(x => new TransportTripAttendanceHistoryDto
            {
                EventType = x.EventType, OccurredAt = x.OccurredAt,
                IsCorrection = x.CorrectsEventId != null
                // CorrectionReason intentionally not exposed to family users.
            }).ToListAsync(ct);
        if (history.Count > 12)
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse(
                "Attendance history exceeds safe bounds.", 409);
        return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.SuccessResponse(history);
    }

    public async Task<ApiResponse<string>> ExportFamilyAttendanceCsvAsync(
        DateOnly date, CancellationToken ct = default)
    {
        // Reuse the exact same live-link authorization and date validation as screen readback.
        var first = await GetFamilyAttendanceAsync(date, 1, 50, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 200)
            return ApiResponse<string>.ErrorResponse("Too many attendance rows; narrow the date.", 409);
        var total = first.Data.TotalCount;
        var rows = new List<TransportFamilyAttendanceDto>(first.Data.Items);
        for (var page = 2; rows.Count < total && page <= 4; page++)
        {
            var next = await GetFamilyAttendanceAsync(date, page, 50, ct);
            if (!next.Success || next.Data == null)
                return ApiResponse<string>.ErrorResponse(next.Message, next.StatusCode);
            if (next.Data.TotalCount != total)
                return ApiResponse<string>.ErrorResponse("Attendance changed; reload before exporting.", 409);
            rows.AddRange(next.Data.Items);
        }
        if (rows.Count != total)
            return ApiResponse<string>.ErrorResponse("Attendance changed; reload before exporting.", 409);
        var csv = new StringBuilder("\uFEFFStudent code,Route,Date,Direction,Attendance,Recorded at UTC\r\n");
        foreach (var row in rows)
            csv.Append(Escape(row.StudentCode)).Append(',')
                .Append(Escape(row.RouteName)).Append(',')
                .Append(Escape(row.OperatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.Direction)).Append(',')
                .Append(Escape(row.AttendanceState)).Append(',')
                .Append(Escape(row.LastEventAt?.ToString("O", CultureInfo.InvariantCulture)))
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
