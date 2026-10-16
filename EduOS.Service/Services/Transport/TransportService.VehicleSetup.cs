using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

/// <summary>Non-monetary vehicle setup over the existing canonical Vehicle aggregate.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<PagedResult<TransportVehicleSetupRowDto>>> GetVehicleSetupPageAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportVehicleSetupRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportVehicleSetupRowDto>>.ErrorResponse("Invalid vehicle filter.");
        var tenant = _currentUser.TenantId;
        var query = from vehicle in _vehicles.GetQueryable().AsNoTracking()
            join campus in _campuses.GetQueryable().AsNoTracking() on vehicle.CampusId equals (long?)campus.Id
            where vehicle.TenantId == tenant && campus.TenantId == tenant &&
                !vehicle.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                (!campusId.HasValue || vehicle.CampusId == campusId.Value)
            select new { vehicle.Id, vehicle.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                vehicle.VehicleNumber, vehicle.VehicleType, vehicle.Capacity, vehicle.IsActive, vehicle.RowVersion };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) ||
                (x.VehicleType != null && x.VehicleType.Contains(term)) || x.CampusName.Contains(term));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        // Keep all active reservations for capacity/closure protection, but show only effective riders as occupied today.
        var reservations = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.State == TransportAssignmentState.Active)
            .GroupBy(x => x.VehicleId)
            .Select(g => new
            {
                VehicleId = g.Key, Reservations = g.Count(),
                OccupiedToday = g.Count(a => a.StartDate <= today &&
                    (!a.EndDate.HasValue || a.EndDate.Value >= today))
            })
            .ToDictionaryAsync(x => x.VehicleId, ct);
        var currentRoutes = await _vehicleRoutes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today))
            .GroupBy(x => x.VehicleId).Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Count, ct);
        return ApiResponse<PagedResult<TransportVehicleSetupRowDto>>.SuccessResponse(
            new PagedResult<TransportVehicleSetupRowDto>
            {
                Items = rows.Select(x => new TransportVehicleSetupRowDto
                {
                    Reference = x.PublicId, CampusId = x.CampusId, CampusName = x.CampusName,
                    VehicleNumber = x.VehicleNumber, VehicleType = x.VehicleType, Capacity = x.Capacity,
                    AssignedStudents = reservations.GetValueOrDefault(x.Id)?.Reservations ?? 0,
                    OccupiedToday = reservations.GetValueOrDefault(x.Id)?.OccupiedToday ?? 0,
                    CurrentRouteAssignments = currentRoutes.GetValueOrDefault(x.Id),
                    IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<TransportVehicleSetupRowDto>> CreateVehicleSetupAsync(
        CreateTransportVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportVehicleSetupRowDto>();
        if (request == null || request.CampusId <= 0 || request.Capacity is < 1 or > 1000 ||
            string.IsNullOrWhiteSpace(request.VehicleNumber) ||
            request.VehicleNumber.Trim().Length > 50 || request.VehicleType?.Trim().Length > 100)
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Campus, vehicle number and valid capacity are required.");
        var tenant = _currentUser.TenantId;
        var number = request.VehicleNumber.Trim().ToUpperInvariant();
        var type = string.IsNullOrWhiteSpace(request.VehicleType) ? null : request.VehicleType.Trim();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token);
                if (campus == null) return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportVehicleSetupRowDto>();
                var existing = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleNumber.ToUpper() == number, token);
                if (existing != null)
                {
                    if (existing.CampusId != campus.Id || existing.Capacity != request.Capacity ||
                        existing.VehicleType != type || !existing.IsActive)
                        return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle number already exists with different details.", 409);
                    var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                    var reserved = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.VehicleId == existing.Id &&
                        x.State == TransportAssignmentState.Active, token);
                    var occupiedToday = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.VehicleId == existing.Id &&
                        x.State == TransportAssignmentState.Active && x.StartDate <= today &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= today), token);
                    var openRoutes = await _vehicleRoutes.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.VehicleId == existing.Id &&
                        x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token);
                    return ApiResponse<TransportVehicleSetupRowDto>.SuccessResponse(
                        MapVehicleSetup(existing, campus.Name, reserved, openRoutes, occupiedToday),
                        "Vehicle already exists; no duplicate created.");
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                var vehicle = new Vehicle
                {
                    TenantId = tenant, CampusId = campus.Id, VehicleNumber = number, VehicleType = type,
                    Capacity = request.Capacity, IsActive = true, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _vehicles.AddAsync(vehicle);
                await _unitOfWork.SaveChangesAsync(token);
                return new ApiResponse<TransportVehicleSetupRowDto>
                {
                    Success = true, StatusCode = 201, Message = "Vehicle created.",
                    Data = MapVehicleSetup(vehicle, campus.Name, 0, 0)
                };
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Vehicle creation conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle creation conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Vehicle creation failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle could not be created.", 500);
        }
    }

    public async Task<ApiResponse<TransportVehicleSetupRowDto>> UpdateVehicleSetupAsync(
        Guid reference, UpdateTransportVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportVehicleSetupRowDto>();
        if (reference == Guid.Empty || request == null || request.Capacity is < 1 or > 1000 ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle, capacity and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var vehicle = await _vehicles.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (vehicle == null) return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle not found.", 404);
                if (!vehicle.CampusId.HasValue)
                    return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Legacy vehicle must be assigned to a campus by Platform.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportVehicleSetupRowDto>();
                if (!vehicle.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle changed. Reload and retry.", 409);
                var occupied = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.State == TransportAssignmentState.Active, token);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var occupiedToday = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.State == TransportAssignmentState.Active && x.StartDate <= today &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= today), token);
                var currentRoutes = await _vehicleRoutes.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token);
                if (request.Capacity < occupied)
                    return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Capacity cannot be below active riders.", 409);
                var currentDriver = _driverLinks != null && await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token);
                if (!request.IsActive && (occupied > 0 || currentRoutes > 0 || currentDriver))
                    return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse(
                        "Close rider, route and driver assignments before deactivating.", 409);
                if (vehicle.Capacity == request.Capacity && vehicle.IsActive == request.IsActive)
                    return ApiResponse<TransportVehicleSetupRowDto>.SuccessResponse(
                        MapVehicleSetup(vehicle, campus.Name, occupied, currentRoutes, occupiedToday), "Vehicle already has the requested settings.");
                vehicle.Capacity = request.Capacity;
                vehicle.IsActive = request.IsActive;
                vehicle.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                vehicle.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<TransportVehicleSetupRowDto>.SuccessResponse(
                    MapVehicleSetup(vehicle, campus.Name, occupied, currentRoutes, occupiedToday), "Vehicle settings updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Vehicle update conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle update conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Vehicle update failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleSetupRowDto>.ErrorResponse("Vehicle settings could not be updated.", 500);
        }
    }

    private static TransportVehicleSetupRowDto MapVehicleSetup(
        Vehicle vehicle, string campusName, int occupied, int currentRoutes, int occupiedToday = 0) => new()
    {
        Reference = vehicle.PublicId, CampusId = vehicle.CampusId ?? 0, CampusName = campusName,
        VehicleNumber = vehicle.VehicleNumber, VehicleType = vehicle.VehicleType,
        Capacity = vehicle.Capacity, AssignedStudents = occupied, OccupiedToday = occupiedToday,
        CurrentRouteAssignments = currentRoutes,
        IsActive = vehicle.IsActive, RowVersion = Convert.ToBase64String(vehicle.RowVersion)
    };
}
