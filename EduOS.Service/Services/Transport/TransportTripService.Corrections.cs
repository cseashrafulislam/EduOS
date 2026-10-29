using Microsoft.Data.SqlClient;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportTripService
{
    public async Task<ApiResponse<TransportTripAttendanceDto>> CorrectAttendanceAsync(
        Guid reference, CorrectTransportTripAttendanceRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage || !(_user.IsTenantAdmin || _user.IsInRole("Principal")))
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Principal or tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.StudentCode) || request.StudentCode.Length > 60 ||
            request.ExpectedState is not ("Dropped" or "NoShow") ||
            request.CorrectedState is not ("Dropped" or "NoShow") ||
            request.ExpectedState == request.CorrectedState ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 15 or > 500)
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Valid rider, terminal outcomes and correction reason required.");
        var tenant = _user.TenantId;
        var reason = request.Reason.Trim();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var trip = await _db.TransportTrips.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference)
                    .Select(x => new { x.Id, x.CampusId, x.State, x.OperatingDate }).FirstOrDefaultAsync(token);
                if (trip == null) return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Trip not found.", 404);
                if (!await HasCampusAsync(trip.CampusId, token))
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Campus access required.", 403);
                if (trip.State != "Completed" || trip.OperatingDate > today ||
                    trip.OperatingDate < today.AddDays(-30))
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Only recently completed trips accept correction.", 409);
                var rider = await _db.TransportTripRiders.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id &&
                    x.StudentCodeSnapshot == request.StudentCode)
                    .Select(x => new { x.Id, x.StudentCodeSnapshot }).SingleOrDefaultAsync(token);
                if (rider == null) return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Rider not found.", 404);
                var prior = await _db.TransportTripAttendanceEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (prior != null)
                {
                    if (prior.TransportTripRiderId != rider.Id || prior.CorrectsEventId == null ||
                        prior.EventType != request.CorrectedState || prior.CorrectionReason != reason)
                        return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Request key reused with different correction.", 409);
                    return ApiResponse<TransportTripAttendanceDto>.SuccessResponse(new TransportTripAttendanceDto
                    { StudentCode = rider.StudentCodeSnapshot, EventType = prior.EventType, OccurredAt = prior.OccurredAt },
                        "Existing correction returned.");
                }
                var last = await _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.TransportTripRiderId == rider.Id)
                    .OrderByDescending(x => x.Sequence)
                    .Select(x => new { x.Id, x.Sequence, x.EventType }).FirstOrDefaultAsync(token);
                if (last == null || last.EventType != request.ExpectedState || last.Sequence >= 12)
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Attendance changed; reload history.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                _db.TransportTripAttendanceEvents.Add(new TransportTripAttendanceEvent
                {
                    TenantId = tenant, TransportTripRiderId = rider.Id,
                    ClientRequestId = request.ClientRequestId, Sequence = Math.Max(3, last.Sequence + 1),
                    EventType = request.CorrectedState, CorrectsEventId = last.Id,
                    CorrectionReason = reason, OccurredAt = now, CreatedBy = _user.UserId
                });
                await _db.SaveChangesAsync(token);
                return new ApiResponse<TransportTripAttendanceDto>
                {
                    Success = true, StatusCode = 201, Message = "Audited correction recorded.",
                    Data = new TransportTripAttendanceDto
                    { StudentCode = rider.StudentCodeSnapshot, EventType = request.CorrectedState, OccurredAt = now }
                };
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport attendance correction conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Concurrent correction; reload.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport trip SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Concurrent trip operation; reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport attendance correction failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Correction failed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>> GetAttendanceHistoryAsync(
        Guid reference, string studentCode, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Transport permission required.", 403);
        if (reference == Guid.Empty || string.IsNullOrWhiteSpace(studentCode) || studentCode.Length > 60)
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Valid trip and rider required.");
        var tenant = _user.TenantId;
        var trip = await _db.TransportTrips.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference &&
            (x.State == "Dispatched" || x.State == "Completed"))
            .Select(x => new { x.Id, x.CampusId }).FirstOrDefaultAsync(ct);
        if (trip == null) return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Trip not found.", 404);
        if (!await HasCampusAsync(trip.CampusId, ct))
            return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Campus access required.", 403);
        var rider = await _db.TransportTripRiders.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id &&
            x.StudentCodeSnapshot == studentCode).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (rider == 0) return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.ErrorResponse("Rider not found.", 404);
        var history = await _db.TransportTripAttendanceEvents.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.TransportTripRiderId == rider)
            .OrderBy(x => x.Sequence).Take(13)
            .Select(x => new TransportTripAttendanceHistoryDto
            {
                EventType = x.EventType, OccurredAt = x.OccurredAt,
                IsCorrection = x.CorrectsEventId != null, CorrectionReason = x.CorrectionReason
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>.SuccessResponse(history);
    }
}
