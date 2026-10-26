using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanShortenUpcomingRouteVehicleDates(
        DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd >= start && requestedEnd < currentEnd.Value;

    // Shortening releases reserved vehicle days without altering existing rider history.
    // Persist request outcome in the same transaction as the canonical link update.
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> ShortenUpcomingRouteVehicleAsync(
        long assignmentId, ShortenUpcomingRouteVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment, new end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.route-vehicle-shorten-upcoming";
        var requestHash = CommandFingerprint(operation, assignmentId,
            request.EffectiveTo.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        var requestId = request.ClientRequestId == Guid.Empty
            ? DeriveRouteVehicleCommandRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash)
            : request.ClientRequestId;
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
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted && x.IsActive, token);
                if (route?.CampusId == null || vehicle?.CampusId != route.CampusId ||
                    vehicle.Capacity < 1 ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == route.CampusId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Active route, vehicle or campus is unavailable.", 409);
                if (!await CanManageCampusAsync(route.CampusId.Value, tenant, token))
                    return Denied<RouteVehicleAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<RouteVehicleAssignmentDto>(tenant,
                    _currentUser.UserId, operation, requestId, assignmentId, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (!link.IsCurrent || link.EffectiveFrom <= today || !link.EffectiveTo.HasValue)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Only a future finite current route/vehicle reservation can be shortened.", 409);
                if (link.EffectiveTo.Value == request.EffectiveTo)
                {
                    var same = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                        MapRouteVehicleLink(link), "Route/vehicle reservation already ends on this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        requestId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                if (!CanShortenUpcomingRouteVehicleDates(
                    link.EffectiveFrom, link.EffectiveTo, request.EffectiveTo, today))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "New end must be within the upcoming finite reservation and earlier than its current end.", 409);

                var oldEnd = link.EffectiveTo.Value;
                // Explicit FK references are historical evidence even when cancelled or soft-deleted.
                // Unlinked active riders using the same route/vehicle are also protected.
                var affectedRiders = await _assignments.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant &&
                    (x.RouteVehicleAssignmentId == link.Id ||
                     (!x.IsDeleted && x.State == TransportAssignmentState.Active &&
                      x.RouteId == link.RouteId && x.VehicleId == link.VehicleId)) &&
                    x.StartDate <= oldEnd &&
                    (!x.EndDate.HasValue || x.EndDate.Value > request.EffectiveTo), token);
                if (affectedRiders)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Rider history or reservations depend on the days being removed. Adjust rider reservations first.", 409);

                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming route/vehicle reservation shortened: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, old end {OldEnd}, new end {NewEnd}",
                    tenant, route.CampusId.Value, link.Id, oldEnd, link.EffectiveTo);
                var success = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                    MapRouteVehicleLink(link), "Upcoming route/vehicle reservation shortened.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    requestId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming route/vehicle shortening conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Concurrent route/vehicle reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Upcoming route/vehicle shortening aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Concurrent route/vehicle reservation conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming route/vehicle shortening failed for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Route/vehicle reservation could not be shortened.", 500);
        }
    }
}
