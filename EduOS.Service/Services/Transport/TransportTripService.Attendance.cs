using Microsoft.Data.SqlClient;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportTripService
{
    public async Task<ApiResponse<TransportTripAttendanceDto>> RecordAttendanceAsync(
        Guid reference, RecordTransportTripAttendanceRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Transport permission required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.StudentCode) || request.StudentCode.Length > 60 ||
            request.EventType is not ("Boarded" or "Dropped" or "NoShow"))
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Valid rider, event and request key required.");
        var tenant = _user.TenantId;
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
                if (trip.State != "Dispatched" || trip.OperatingDate != today)
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Only today's dispatched trip accepts attendance.", 409);
                var rider = await _db.TransportTripRiders.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id &&
                    x.StudentCodeSnapshot == request.StudentCode)
                    .Select(x => new { x.Id, x.StudentCodeSnapshot }).SingleOrDefaultAsync(token);
                if (rider == null) return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Rider not found.", 404);
                var prior = await _db.TransportTripAttendanceEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (prior != null)
                {
                    if (prior.TransportTripRiderId != rider.Id || prior.EventType != request.EventType)
                        return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Request key reused for a different event.", 409);
                    return ApiResponse<TransportTripAttendanceDto>.SuccessResponse(new TransportTripAttendanceDto
                    { StudentCode = rider.StudentCodeSnapshot, EventType = prior.EventType, OccurredAt = prior.OccurredAt },
                        "Existing attendance event returned.");
                }
                var existing = await _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.TransportTripRiderId == rider.Id && !x.IsDeleted)
                    .OrderBy(x => x.Sequence).Select(x => new { x.Sequence, x.EventType })
                    .Take(3).ToListAsync(token);
                var first = existing.SingleOrDefault(x => x.Sequence == 1);
                var second = existing.SingleOrDefault(x => x.Sequence == 2);
                if (existing.Count > 2 || (second != null && (first?.EventType != "Boarded" || second.EventType != "Dropped")))
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Attendance history integrity conflict.", 409);
                var sequence = request.EventType == "Dropped" ? 2 : 1;
                if (sequence == 1 && existing.Count != 0 ||
                    sequence == 2 && (first?.EventType != "Boarded" || second != null))
                    return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Attendance transition is not allowed.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                _db.TransportTripAttendanceEvents.Add(new TransportTripAttendanceEvent
                {
                    TenantId = tenant, TransportTripRiderId = rider.Id, ClientRequestId = request.ClientRequestId,
                    Sequence = sequence, EventType = request.EventType, OccurredAt = now, CreatedBy = _user.UserId
                });
                await _db.SaveChangesAsync(token);
                return new ApiResponse<TransportTripAttendanceDto>
                {
                    Success = true, StatusCode = 201, Message = "Attendance event recorded.",
                    Data = new TransportTripAttendanceDto
                    { StudentCode = rider.StudentCodeSnapshot, EventType = request.EventType, OccurredAt = now }
                };
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport attendance uniqueness conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Concurrent attendance update; reload.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport trip SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Concurrent trip operation; reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport attendance failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripAttendanceDto>.ErrorResponse("Attendance could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<TransportFamilyAttendanceDto>>> GetFamilyAttendanceAsync(
        DateOnly date, int page, int pageSize, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.UserId <= 0 || _user.TenantId <= 0 ||
            !(_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent")))
            return ApiResponse<PagedResult<TransportFamilyAttendanceDto>>.ErrorResponse("Family access required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (date < today.AddDays(-30) || date > today || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportFamilyAttendanceDto>>.ErrorResponse("Invalid date or paging.");
        var tenant = _user.TenantId;
        var ownStudents = _db.Students.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.UserId == _user.UserId).Select(x => x.Id);
        var linkedStudents = from link in _db.StudentGuardians.AsNoTracking()
            join guardian in _db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where link.TenantId == tenant && !link.IsDeleted && link.IsAuthorizedPickup &&
                guardian.TenantId == tenant && !guardian.IsDeleted && guardian.IsActive &&
                guardian.UserId == _user.UserId
            select link.StudentId;
        IQueryable<long> visible = _user.IsInRole("Student")
            ? (_user.IsInRole("Guardian") || _user.IsInRole("Parent") ? ownStudents.Union(linkedStudents) : ownStudents)
            : linkedStudents;
        var query = from rider in _db.TransportTripRiders.AsNoTracking()
            join trip in _db.TransportTrips.AsNoTracking() on rider.TransportTripId equals trip.Id
            join route in _db.Routes.AsNoTracking() on trip.RouteId equals route.Id
            where rider.TenantId == tenant && !rider.IsDeleted && visible.Contains(rider.StudentId) &&
                trip.TenantId == tenant && !trip.IsDeleted &&
                (trip.State == "Dispatched" || trip.State == "Completed") &&
                trip.OperatingDate == date && route.TenantId == tenant
            select new
            {
                rider.Id, rider.StudentCodeSnapshot, trip.OperatingDate, trip.Direction,
                RouteName = route.Name,
                LastType = _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && !e.IsDeleted && e.TransportTripRiderId == rider.Id)
                    .OrderByDescending(e => e.Sequence).Select(e => e.EventType).FirstOrDefault(),
                LastAt = _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && !e.IsDeleted && e.TransportTripRiderId == rider.Id)
                    .OrderByDescending(e => e.Sequence).Select(e => (DateTime?)e.OccurredAt).FirstOrDefault()
            };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.StudentCodeSnapshot).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportFamilyAttendanceDto>>.SuccessResponse(
            new PagedResult<TransportFamilyAttendanceDto>
            {
                Items = rows.Select(x => new TransportFamilyAttendanceDto
                {
                    StudentCode = x.StudentCodeSnapshot, RouteName = x.RouteName,
                    OperatingDate = x.OperatingDate, Direction = x.Direction,
                    AttendanceState = x.LastType ?? "Awaiting", LastEventAt = x.LastAt
                }).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
    }
}
