using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Transport;

/// <summary>
/// Tenant-admin review of pre-campus transport data. A record with ANY historical
/// rider, route/vehicle or driver link cannot be reassigned by this workflow.
/// It requires a separate reconciliation of the original records and Finance impact.
/// </summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportCampusRepairReviewDto>> GetLegacyCampusRepairReviewAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsTenantAdmin || _currentUser.TenantId <= 0)
            return Denied<TransportCampusRepairReviewDto>();
        if (_driverLinks == null)
            return ApiResponse<TransportCampusRepairReviewDto>.ErrorResponse("Driver history is unavailable for safe campus review.", 503);
        var tenant = _currentUser.TenantId;
        const int limit = 100;
        var routes = await _routes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.CampusId == null)
            .OrderBy(x => x.Id).Take(limit + 1)
            .Select(x => new { x.Id, x.PublicId, x.Code, x.Name, x.RowVersion })
            .ToListAsync(cancellationToken);
        var vehicles = await _vehicles.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.CampusId == null)
            .OrderBy(x => x.Id).Take(limit + 1)
            .Select(x => new { x.Id, x.PublicId, x.VehicleNumber, x.RowVersion })
            .ToListAsync(cancellationToken);
        var routeIds = routes.Take(limit).Select(x => x.Id).ToArray();
        var vehicleIds = vehicles.Take(limit).Select(x => x.Id).ToArray();
        var riderRoutes = await _assignments.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && routeIds.Contains(x.RouteId))
            .GroupBy(x => x.RouteId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var riderVehicles = await _assignments.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && vehicleIds.Contains(x.VehicleId))
            .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var routeLinks = await _vehicleRoutes.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && routeIds.Contains(x.RouteId))
            .GroupBy(x => x.RouteId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var vehicleLinks = await _vehicleRoutes.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && vehicleIds.Contains(x.VehicleId))
            .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var driverLinks = await _driverLinks.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant && vehicleIds.Contains(x.VehicleId))
                .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var rows = routes.Take(limit).Select(x => new TransportCampusRepairRowDto
        {
            Kind = "route", Reference = x.PublicId, Label = x.Code + " — " + x.Name,
            RowVersion = Convert.ToBase64String(x.RowVersion),
            LinkedRecords = riderRoutes.GetValueOrDefault(x.Id) + routeLinks.GetValueOrDefault(x.Id)
        }).Concat(vehicles.Take(limit).Select(x => new TransportCampusRepairRowDto
        {
            Kind = "vehicle", Reference = x.PublicId, Label = x.VehicleNumber,
            RowVersion = Convert.ToBase64String(x.RowVersion),
            LinkedRecords = riderVehicles.GetValueOrDefault(x.Id) + vehicleLinks.GetValueOrDefault(x.Id) +
                driverLinks.GetValueOrDefault(x.Id)
        })).ToArray();
        return ApiResponse<TransportCampusRepairReviewDto>.SuccessResponse(new()
        {
            Rows = rows, HasMore = routes.Count > limit || vehicles.Count > limit
        });
    }

    public async Task<ApiResponse<bool>> ApplyLegacyCampusRepairAsync(string kind, Guid reference,
        ApplyTransportCampusRepairDto request, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsTenantAdmin || _currentUser.TenantId <= 0)
            return Denied<bool>();
        if (_driverLinks == null)
            return ApiResponse<bool>.ErrorResponse("Driver history is unavailable for safe campus review.", 503);
        if (kind is not ("route" or "vehicle") || reference == Guid.Empty ||
            request == null || request.CampusId <= 0 || !request.ConfirmHistoricalCampusReview ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<bool>.ErrorResponse("A valid record, version, campus and explicit approval are required.", 400);
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var campusExists = await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, ct);
                if (!campusExists) return ApiResponse<bool>.ErrorResponse("Active institution campus not found.", 404);
                if (kind == "route")
                {
                    var route = await _routes.GetQueryable().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                    if (route == null) return ApiResponse<bool>.ErrorResponse("Route not found.", 404);
                    if (route.CampusId != null || !route.RowVersion.AsSpan().SequenceEqual(version))
                        return ApiResponse<bool>.ErrorResponse("Route changed. Refresh the review before applying.", 409);
                    if (await _assignments.GetQueryable().IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.RouteId == route.Id, ct) ||
                        await _vehicleRoutes.GetQueryable().IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.RouteId == route.Id, ct))
                        return ApiResponse<bool>.ErrorResponse("Route has historical links. Separate reconciliation and Finance review are required.", 409);
                    route.CampusId = request.CampusId;
                    route.UpdatedBy = _currentUser.UserId;
                    route.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                }
                else
                {
                    var vehicle = await _vehicles.GetQueryable().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                    if (vehicle == null) return ApiResponse<bool>.ErrorResponse("Vehicle not found.", 404);
                    if (vehicle.CampusId != null || !vehicle.RowVersion.AsSpan().SequenceEqual(version))
                        return ApiResponse<bool>.ErrorResponse("Vehicle changed. Refresh the review before applying.", 409);
                    if (await _assignments.GetQueryable().IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.VehicleId == vehicle.Id, ct) ||
                        await _vehicleRoutes.GetQueryable().IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.VehicleId == vehicle.Id, ct) ||
                        await _driverLinks.GetQueryable().IgnoreQueryFilters().AnyAsync(x =>
                            x.TenantId == tenant && x.VehicleId == vehicle.Id, ct))
                        return ApiResponse<bool>.ErrorResponse("Vehicle has historical links. Separate reconciliation and Finance review are required.", 409);
                    vehicle.CampusId = request.CampusId;
                    vehicle.UpdatedBy = _currentUser.UserId;
                    vehicle.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                }
                // DbContext.SaveChanges creates a tenant-scoped old/new-value audit log
                // inside this transaction; no migration is run on application startup.
                await _unitOfWork.SaveChangesAsync(ct);
                return ApiResponse<bool>.SuccessResponse(true, "Campus assignment applied and audited.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Record changed. Refresh and review again.", 409);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Campus repair conflicted with another operation.", 409);
        }
    }
}
