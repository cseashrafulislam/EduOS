using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanChangeUpcomingRouteVehicleVehicleDates(
        DateOnly start, DateOnly? end, DateOnly today) =>
        start > today && (!end.HasValue || end.Value >= start);

    /// <summary>Eligible same-campus vehicles for a future route assignment, excluding
    /// overlapping vehicle reservations and active rider commitments. No student data is returned.</summary>
    public async Task<ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>> GetUpcomingRouteVehicleChoicesAsync(
        long assignmentId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<UpcomingRouteVehicleChoiceDto>>();
        if (assignmentId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.ErrorResponse("Invalid vehicle search.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var link = await _vehicleRoutes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, ct);
        if (link == null)
            return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.ErrorResponse("Assignment not found.", 404);
        var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == link.RouteId && !x.IsDeleted && x.IsActive, ct);
        if (route?.CampusId == null ||
            !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, ct))
            return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.ErrorResponse("Route campus unavailable.", 409);
        if (!await CanManageCampusAsync(route.CampusId.Value, tenant, ct))
            return Denied<PagedResult<UpcomingRouteVehicleChoiceDto>>();
        if (!link.IsCurrent || !CanChangeUpcomingRouteVehicleVehicleDates(
            link.EffectiveFrom, link.EffectiveTo, today))
            return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.ErrorResponse(
                "Only upcoming route/vehicle assignments can change vehicles.", 409);
        var end = link.EffectiveTo ?? DateOnly.MaxValue;
        // Do not change a canonical link already referenced by any historical or scheduled rider.
        if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.RouteVehicleAssignmentId == link.Id, ct) ||
            await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                x.State == TransportAssignmentState.Active &&
                x.RouteVehicleAssignmentId == null && x.RouteId == link.RouteId &&
                x.VehicleId == link.VehicleId && x.StartDate <= end &&
                (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), ct))
            return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.ErrorResponse(
                "Riders reference this route/vehicle reservation. Move riders first.", 409);
        var query = _vehicles.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
            x.CampusId == route.CampusId && x.Capacity > 0 && x.Id != link.VehicleId &&
            !_vehicleRoutes.GetQueryable().Any(a =>
                a.TenantId == tenant && !a.IsDeleted && a.IsCurrent && a.VehicleId == x.Id &&
                a.EffectiveFrom <= end &&
                (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= link.EffectiveFrom)) &&
            !_assignments.GetQueryable().Any(a =>
                a.TenantId == tenant && !a.IsDeleted &&
                a.State == TransportAssignmentState.Active && a.VehicleId == x.Id &&
                a.StartDate <= end &&
                (!a.EndDate.HasValue || a.EndDate.Value >= link.EffectiveFrom)));
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) ||
                (x.VehicleType != null && x.VehicleType.Contains(term)));
        var count = await query.CountAsync(ct);
        var vehicles = await query.OrderBy(x => x.VehicleNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.VehicleNumber, x.Capacity, x.RowVersion }).ToListAsync(ct);
        return ApiResponse<PagedResult<UpcomingRouteVehicleChoiceDto>>.SuccessResponse(
            new PagedResult<UpcomingRouteVehicleChoiceDto>
            {
                Items = vehicles.Select(x => new UpcomingRouteVehicleChoiceDto
                {
                    VehicleId = x.Id, VehicleNumber = x.VehicleNumber, Capacity = x.Capacity,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Change the planned vehicle on the existing canonical route/vehicle link.
    /// Existing rider references are immutable. Durable request-key replay stores
    /// its outcome atomically with the canonical vehicle change.</summary>
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> ChangeUpcomingRouteVehicleAsync(
        long assignmentId, ChangeUpcomingRouteVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.NewVehicleId <= 0 ||
            !TryDecodeVersion(request.AssignmentRowVersion, out var assignmentVersion) ||
            !TryDecodeVersion(request.VehicleRowVersion, out var vehicleVersion))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment, replacement vehicle and both row versions are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.route-vehicle-change";
        var requestHash = CommandFingerprint(operation, assignmentId, request.NewVehicleId,
            Convert.ToBase64String(assignmentVersion), Convert.ToBase64String(vehicleVersion));
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var link = await _vehicleRoutes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, token);
                if (link == null)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.RouteId && !x.IsDeleted && x.IsActive, token);
                if (route?.CampusId == null ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == route.CampusId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Route campus unavailable.", 409);
                if (!await CanManageCampusAsync(route.CampusId.Value, tenant, token))
                    return Denied<RouteVehicleAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<RouteVehicleAssignmentDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, assignmentId, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (!link.IsCurrent || !CanChangeUpcomingRouteVehicleVehicleDates(
                    link.EffectiveFrom, link.EffectiveTo, today))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Only upcoming route/vehicle assignments can change vehicles.", 409);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.NewVehicleId &&
                    !x.IsDeleted && x.IsActive && x.Capacity > 0, token);
                if (vehicle?.CampusId != route.CampusId)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Replacement vehicle must be active and in the same campus.", 409);
                if (link.VehicleId == vehicle.Id)
                {
                    var same = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                        MapRouteVehicleLink(link), "This vehicle is already assigned.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        request.ClientRequestId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(assignmentVersion) ||
                    !vehicle.RowVersion.AsSpan().SequenceEqual(vehicleVersion))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment or vehicle changed. Reload and retry.", 409);
                var end = link.EffectiveTo ?? DateOnly.MaxValue;
                // Include deleted/historical linked riders: changing the vehicle would rewrite their provenance.
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.RouteVehicleAssignmentId == link.Id, token) ||
                    await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted &&
                        x.State == TransportAssignmentState.Active &&
                        x.RouteVehicleAssignmentId == null && x.RouteId == link.RouteId &&
                        x.VehicleId == link.VehicleId && x.StartDate <= end &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Riders reference this assignment. Move riders first.", 409);
                if (await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.Id != link.Id && x.VehicleId == vehicle.Id &&
                    x.EffectiveFrom <= end &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= link.EffectiveFrom), token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Replacement vehicle is reserved on another route.", 409);
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted &&
                    x.State == TransportAssignmentState.Active && x.VehicleId == vehicle.Id &&
                    x.StartDate <= end &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Replacement vehicle has existing rider reservations.", 409);
                var oldVehicleId = link.VehicleId;
                link.VehicleId = vehicle.Id;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming route vehicle changed tenant {TenantId} campus {CampusId} link {LinkId} old {OldVehicle} new {NewVehicle}",
                    tenant, route.CampusId.Value, link.Id, oldVehicleId, vehicle.Id);
                var success = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                    MapRouteVehicleLink(link), "Upcoming route/vehicle assignment updated.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment changed. Reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route vehicle change conflict tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent vehicle reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Route vehicle change aborted tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent vehicle reservation conflict.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Route vehicle change SQL concurrency conflict tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent vehicle reservation conflict.", 409);
        }
        catch (InvalidOperationException ex) when (HasSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Route vehicle change SQL serialization conflict tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent vehicle reservation conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route vehicle change failed tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Vehicle change failed.", 500);
        }
    }
}
