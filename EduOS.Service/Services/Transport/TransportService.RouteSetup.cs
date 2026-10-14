using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Service.Services.Transport;

/// <summary>Creates the canonical Route aggregate with ordered stops; fare writes are Finance-owned.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportRouteSetupRowDto>> CreateRouteWithStopsAsync(
        CreateTransportRouteRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteSetupRowDto>();
        if (request == null || request.CampusId <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Code) || request.Name.Trim().Length > 150 ||
            request.Code.Trim().Length > 50 || request.Stops == null ||
            request.Stops.Count is < 1 or > 50 ||
            request.Stops.Any(s => s == null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Trim().Length > 150))
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Campus, route name/code and 1–50 valid stops are required.");
        var names = request.Stops.Select(s => s.Name.Trim()).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Stop names must be unique within a route.");
        var name = request.Name.Trim();
        var code = request.Code.Trim().ToUpperInvariant();
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token))
                    return Denied<TransportRouteSetupRowDto>();
                var existing = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.CampusId == campus.Id &&
                    x.Code.ToUpper() == code, token);
                if (existing != null)
                {
                    var savedStops = await _stops.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && !x.IsDeleted && x.RouteId == existing.Id)
                        .OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).ToListAsync(token);
                    var same = existing.IsActive && existing.Name == name &&
                        savedStops.Count == names.Length &&
                        savedStops.Select((s, i) => s.IsActive && s.SequenceNo == i + 1 &&
                            string.Equals(s.Name, names[i], StringComparison.OrdinalIgnoreCase)).All(x => x);
                    if (!same) return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse(
                        "Route code already exists with different details.", 409);
                    return ApiResponse<TransportRouteSetupRowDto>.SuccessResponse(new TransportRouteSetupRowDto
                    {
                        Reference = existing.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                        Name = existing.Name, Code = existing.Code, StopCount = savedStops.Count,
                        IsActive = existing.IsActive, RowVersion = Convert.ToBase64String(existing.RowVersion)
                    }, "Route already exists; no duplicate created.");
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                var route = new TransportRoute
                {
                    TenantId = tenant, CampusId = campus.Id, Name = name, Code = code,
                    IsActive = true, CreatedAt = now, CreatedBy = _currentUser.UserId
                    // DefaultFare intentionally untouched; Finance owns monetary settings.
                };
                await _routes.AddAsync(route);
                await _unitOfWork.SaveChangesAsync(token);
                for (var i = 0; i < names.Length; i++)
                    await _stops.AddAsync(new RouteStop
                    {
                        TenantId = tenant, RouteId = route.Id, Name = names[i], SequenceNo = i + 1,
                        IsActive = true, CreatedAt = now, CreatedBy = _currentUser.UserId
                        // FareFromOrigin intentionally unset.
                    });
                await _unitOfWork.SaveChangesAsync(token);
                return new ApiResponse<TransportRouteSetupRowDto>
                {
                    Success = true, StatusCode = 201, Message = "Route and ordered stops created.",
                    Data = new TransportRouteSetupRowDto
                    {
                        Reference = route.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                        Name = route.Name, Code = route.Code, StopCount = names.Length,
                        IsActive = true, RowVersion = Convert.ToBase64String(route.RowVersion)
                    }
                };
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport route creation conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route creation conflicts with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transport route creation failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route creation failed.", 500);
        }
    }
}
