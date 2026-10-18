using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Service.Services.Transport;

/// <summary>Maintains the canonical RouteStop children without changing rider stop IDs or Finance-owned fares.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportRouteStopsEditorDto>> GetRouteStopsForEditAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteStopsEditorDto>();
        if (reference == Guid.Empty)
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route reference is required.");
        var tenant = _currentUser.TenantId;
        var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.PublicId == reference, ct);
        if (route == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active route not found.", 404);
        if (!route.CampusId.HasValue)
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Legacy route needs campus assignment by Platform.", 409);
        var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, ct);
        if (campus == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active campus not found.", 409);
        if (!await CanManageCampusAsync(campus.Id, tenant, ct)) return Denied<TransportRouteStopsEditorDto>();
        var stops = await _stops.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id)
            .OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).ToListAsync(ct);
        return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(MapRouteStopsEditor(route, stops));
    }

    public async Task<ApiResponse<TransportRouteStopsEditorDto>> UpdateRouteStopsAsync(
        Guid reference, UpdateTransportRouteStopsRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteStopsEditorDto>();
        if (reference == Guid.Empty || request == null || !TryDecodeVersion(request.RowVersion, out var routeVersion) ||
            request.Stops == null || request.Stops.Count is < 1 or > 50 ||
            request.Stops.Any(x => x == null || x.Id <= 0 || string.IsNullOrWhiteSpace(x.Name) ||
                x.Name.Trim().Length > 150 || !TryDecodeVersion(x.RowVersion, out _)))
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route version and 1–50 valid existing stops are required.");
        var requested = request.Stops;
        if (requested.Select(x => x.Id).Distinct().Count() != requested.Count ||
            requested.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count)
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop IDs and names must be unique.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var route = await _routes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.PublicId == reference, token);
                if (route == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active route not found.", 404);
                if (!route.CampusId.HasValue)
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Legacy route needs campus assignment by Platform.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active campus not found.", 409);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportRouteStopsEditorDto>();
                var stops = await _stops.GetQueryable()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id)
                    .OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).ToListAsync(token);
                if (stops.Count != requested.Count ||
                    !stops.Select(x => x.Id).OrderBy(x => x).SequenceEqual(requested.Select(x => x.Id).OrderBy(x => x)))
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse(
                        "Stop membership changed. Reload; existing rider stop references cannot be replaced.", 409);
                var byId = stops.ToDictionary(x => x.Id);
                var same = requested.Select((item, index) => new
                {
                    Existing = byId[item.Id], Name = item.Name.Trim(), Sequence = index + 1
                }).All(x => x.Existing.Name == x.Name && x.Existing.SequenceNo == x.Sequence);
                // PUT is idempotent: a completed retry with the same desired state is a no-op.
                if (same) return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                    MapRouteStopsEditor(route, stops), "Route stops already match the requested order.");
                if (!route.RowVersion.AsSpan().SequenceEqual(routeVersion))
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route changed. Reload and retry.", 409);
                foreach (var item in requested)
                {
                    TryDecodeVersion(item.RowVersion, out var stopVersion);
                    if (!byId[item.Id].RowVersion.AsSpan().SequenceEqual(stopVersion))
                        return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop changed. Reload and retry.", 409);
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                var reordered = requested.Select((item, index) => new { Stop = byId[item.Id], FinalSequence = index + 1 })
                    .Where(x => x.Stop.SequenceNo != x.FinalSequence).ToList();
                if (reordered.Count > 0)
                {
                    // A single EF SaveChanges can violate the existing unique (Tenant, Route, Sequence)
                    // index while swapping two stops. Stage moved rows above ALL existing sequence
                    // numbers (including soft-deleted rows), then apply final order in the same transaction.
                    var maximum = await _stops.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && x.RouteId == route.Id)
                        .Select(x => (int?)x.SequenceNo).MaxAsync(token) ?? 0;
                    if (maximum > int.MaxValue - reordered.Count)
                        return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop sequence range exhausted.", 409);
                    var occupiedByDeleted = await _stops.GetQueryable().AsNoTracking()
                        .AnyAsync(x => x.TenantId == tenant && x.RouteId == route.Id && x.IsDeleted &&
                            x.SequenceNo >= 1 && x.SequenceNo <= requested.Count, token);
                    if (occupiedByDeleted)
                        return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse(
                            "Soft-deleted stop sequence conflicts with active order; Platform index migration required.", 409);
                    var next = Math.Max(0, maximum);
                    foreach (var entry in reordered)
                    {
                        entry.Stop.SequenceNo = ++next;
                        entry.Stop.UpdatedAt = now;
                        entry.Stop.UpdatedBy = _currentUser.UserId;
                    }
                    await _unitOfWork.SaveChangesAsync(token);
                }
                for (var i = 0; i < requested.Count; i++)
                {
                    var stop = byId[requested[i].Id];
                    var name = requested[i].Name.Trim();
                    if (stop.Name == name && stop.SequenceNo == i + 1) continue;
                    stop.Name = name; stop.SequenceNo = i + 1;
                    stop.UpdatedAt = now; stop.UpdatedBy = _currentUser.UserId;
                }
                route.UpdatedAt = now; route.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                    MapRouteStopsEditor(route, stops), "Route stop names and order updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route or stop changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route stop update conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route stop update conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route stop update failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route stops could not be updated.", 500);
        }
    }

    private static TransportRouteStopsEditorDto MapRouteStopsEditor(TransportRoute route, IEnumerable<RouteStop> stops) => new()
    {
        Reference = route.PublicId, Name = route.Name, RowVersion = Convert.ToBase64String(route.RowVersion),
        Stops = stops.OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).Select(x => new TransportRouteStopEditorRowDto
        {
            Id = x.Id, Name = x.Name, SequenceNo = x.SequenceNo, IsActive = x.IsActive,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList()
    };
}
