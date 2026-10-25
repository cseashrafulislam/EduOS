using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Return only same-route, same-campus vehicles linked for the entire reservation period; no rider identities.</summary>
    public async Task<ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>> GetUpcomingVehicleOptionsAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<UpcomingTransportVehicleOptionDto>>();
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Invalid assignment or paging.");
        var tenant = _currentUser.TenantId;
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
        if (assignment == null) return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Assignment not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
            x.StudentId == assignment.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
        if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Enrollment campus unavailable.", 409);
        if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
            return Denied<PagedResult<UpcomingTransportVehicleOptionDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (assignment.State != TransportAssignmentState.Active || assignment.StartDate <= today ||
            (assignment.EndDate.HasValue && assignment.EndDate.Value < assignment.StartDate))
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Only upcoming active assignments can change vehicles.", 409);
        var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId && x.IsActive &&
            x.CampusId == enrollment.CampusId, ct);
        if (route == null)
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Route is unavailable for this campus.", 409);
        var options = from link in _vehicleRoutes.GetQueryable().AsNoTracking()
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
            where link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                link.RouteId == route.Id && link.EffectiveFrom <= assignment.StartDate &&
                (!link.EffectiveTo.HasValue || (assignment.EndDate.HasValue && link.EffectiveTo.Value >= assignment.EndDate.Value)) &&
                vehicle.TenantId == tenant && !vehicle.IsDeleted && vehicle.IsActive &&
                vehicle.CampusId == enrollment.CampusId && vehicle.Capacity > 0
            select new { LinkId = link.Id, vehicle.PublicId, VehicleId = vehicle.Id, vehicle.VehicleNumber, vehicle.Capacity };
        var total = await options.CountAsync(ct);
        var rows = await options.OrderBy(x => x.VehicleNumber).ThenBy(x => x.LinkId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var vehicleIds = rows.Select(x => x.VehicleId).Distinct().ToArray();
        // Seat demand is the peak simultaneous occupancy within the requested interval,
        // not the number of reservations ever recorded against the vehicle.
        var requestedEnd = assignment.EndDate ?? DateOnly.MaxValue;
        var overlapping = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                x.State == TransportAssignmentState.Active && x.Id != assignment.Id &&
                vehicleIds.Contains(x.VehicleId) && x.StartDate <= requestedEnd &&
                (!x.EndDate.HasValue || x.EndDate.Value >= assignment.StartDate))
            .Select(x => new { x.VehicleId, x.StartDate, x.EndDate })
            .Take(10001).ToListAsync(ct);
        if (overlapping.Count > 10000)
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse(
                "Too many overlapping reservations. Narrow the vehicle selection.", 413);
        var counts = overlapping.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => PeakReservedSeats(
                g.Select(x => (x.StartDate, x.EndDate)), assignment.StartDate, requestedEnd).Peak);
        return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.SuccessResponse(
            new PagedResult<UpcomingTransportVehicleOptionDto>
            {
                Items = rows.Select(x => new UpcomingTransportVehicleOptionDto
                {
                    VehicleReference = x.PublicId, VehicleNumber = x.VehicleNumber,
                    Capacity = x.Capacity, ReservedSeats = counts.GetValueOrDefault(x.VehicleId),
                    IsCurrent = x.VehicleId == assignment.VehicleId,
                    Available = x.VehicleId == assignment.VehicleId || counts.GetValueOrDefault(x.VehicleId) < x.Capacity
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Move a pre-start rider to another vehicle on the SAME route without touching fares.</summary>
    public async Task<ApiResponse<StudentTransportDto>> ChangeUpcomingVehicleAsync(
        Guid reference, ChangeUpcomingTransportVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null || request.VehicleReference == Guid.Empty ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment, vehicle and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var assignment = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (assignment == null) return Error("Assignment not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
                    x.StudentId == assignment.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, token);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, token))
                    return Error("Active enrollment campus unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, token))
                    return Denied<StudentTransportDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (assignment.State != TransportAssignmentState.Active || assignment.StartDate <= today ||
                    (assignment.EndDate.HasValue && assignment.EndDate.Value < assignment.StartDate))
                    return Error("Only upcoming active assignments can change vehicles.", 409);
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId && x.IsActive &&
                    x.CampusId == enrollment.CampusId, token);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == request.VehicleReference &&
                    x.IsActive && x.CampusId == enrollment.CampusId && x.Capacity > 0, token);
                if (route == null || vehicle == null)
                    return Error("Active route/vehicle is unavailable in this campus.", 409);
                var pairing = await _vehicleRoutes.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                        x.RouteId == route.Id && x.VehicleId == vehicle.Id &&
                        x.EffectiveFrom <= assignment.StartDate &&
                        (!x.EffectiveTo.HasValue || (assignment.EndDate.HasValue && x.EffectiveTo.Value >= assignment.EndDate.Value)))
                    .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(token);
                if (pairing == null) return Error("Vehicle is not assigned to this route for the reservation period.", 409);
                // A retry after a successful update is a no-op, even if its old concurrency token is stale.
                if (assignment.VehicleId == vehicle.Id && assignment.RouteVehicleAssignmentId == pairing.Id)
                    return ApiResponse<StudentTransportDto>.SuccessResponse(
                        await MapAsync(assignment, token), "Vehicle already matches this reservation.");
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                // Recheck under the serializable write transaction: disjoint bookings
                // do not occupy a seat simultaneously, but inclusive overlaps do.
                var requestedEnd = assignment.EndDate ?? DateOnly.MaxValue;
                var overlapping = await _assignments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.Id != assignment.Id &&
                        x.VehicleId == vehicle.Id && x.State == TransportAssignmentState.Active &&
                        x.StartDate <= requestedEnd &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= assignment.StartDate))
                    .Select(x => new { x.StartDate, x.EndDate })
                    .Take(10001).ToListAsync(token);
                if (overlapping.Count > 10000)
                    return Error("Too many overlapping reservations to validate capacity.", 413);
                var reserved = PeakReservedSeats(
                    overlapping.Select(x => (x.StartDate, x.EndDate)),
                    assignment.StartDate, requestedEnd).Peak;
                if (reserved >= vehicle.Capacity)
                    return Error("Destination vehicle has no available seats.", 409);
                assignment.VehicleId = vehicle.Id;
                assignment.RouteVehicleAssignmentId = pairing.Id;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation("Transport pre-start vehicle changed: tenant {TenantId}, assignment {AssignmentId}", tenant, assignment.Id);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, token), "Upcoming transport vehicle changed.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error("Assignment changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming vehicle reassignment conflict for tenant {TenantId}", tenant);
            return Error("Vehicle reassignment conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming vehicle reassignment failed for tenant {TenantId}", tenant);
            return Error("Transport vehicle could not be changed.", 500);
        }
    }
}
