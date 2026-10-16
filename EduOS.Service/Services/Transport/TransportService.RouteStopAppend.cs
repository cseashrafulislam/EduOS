using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportRouteStopsEditorDto>> AppendRouteStopAsync(
        Guid reference, AppendTransportRouteStopRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteStopsEditorDto>();
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop name and route version are required.");
        var tenant = _currentUser.TenantId;
        var name = request.Name.Trim();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var route = await _routes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.PublicId == reference, token);
                if (route == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active route not found.", 404);
                if (!route.CampusId.HasValue)
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Legacy route requires campus assignment.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active campus not found.", 409);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportRouteStopsEditorDto>();
                var stops = await _stops.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.RouteId == route.Id && !x.IsDeleted)
                    .OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).ToListAsync(token);
                if (stops.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                        MapRouteStopsEditor(route, stops), "Stop already exists.");
                if (stops.Count >= 50)
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Maximum 50 stops per route.", 409);
                if (!route.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route changed. Reload.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var stop = new RouteStop
                {
                    TenantId = tenant, RouteId = route.Id, Name = name,
                    SequenceNo = stops.Count == 0 ? 1 : stops.Max(x => x.SequenceNo) + 1,
                    IsActive = true, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _stops.AddAsync(stop);
                route.UpdatedAt = now; route.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                stops.Add(stop);
                return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                    MapRouteStopsEditor(route, stops), "Stop added.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route changed. Reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Stop addition conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop addition conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Stop addition failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop could not be added.", 500);
        }
    }
}
