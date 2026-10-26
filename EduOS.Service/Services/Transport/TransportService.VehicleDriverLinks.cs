using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

/// <summary>Vehicle/driver assignments use the canonical aggregate and existing filtered unique indexes.
/// Only one current assignment per vehicle and driver can be scheduled until Platform changes the indexes.
/// ClientRequestId is not persisted on this entity: exact-data replay is supported, durable key-based
/// idempotency requires a Platform-owned schema change and must not be claimed here.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<PagedResult<TransportVehicleDriverLinkRowDto>>> GetVehicleDriverLinksAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default, bool scheduledOnly = false)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportVehicleDriverLinkRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportVehicleDriverLinkRowDto>>.ErrorResponse("Invalid assignment filter.");
        var tenant = _currentUser.TenantId;
        var query = from link in _driverLinks.GetQueryable().AsNoTracking()
                    join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
                    join driver in _drivers.GetQueryable().AsNoTracking() on link.TransportDriverId equals driver.Id
                    join campus in _campuses.GetQueryable().AsNoTracking() on vehicle.CampusId equals (long?)campus.Id
                    where link.TenantId == tenant && vehicle.TenantId == tenant && driver.TenantId == tenant &&
                          campus.TenantId == tenant && !link.IsDeleted && !vehicle.IsDeleted &&
                          !driver.IsDeleted && !campus.IsDeleted &&
                          (!campusId.HasValue || campus.Id == campusId.Value)
                    select new { link.Id, link.VehicleId, link.TransportDriverId, link.EffectiveFrom,
                        link.EffectiveTo, link.IsCurrent, link.RowVersion, CampusId = campus.Id,
                        CampusName = campus.Name, vehicle.VehicleNumber, DriverName = driver.FullName,
                        driver.LicenseExpiresOn };
        if (scheduledOnly) query = query.Where(x => x.IsCurrent && x.EffectiveFrom > DateOnly.FromDateTime(_clock.GetLocalNow().DateTime));
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) || x.DriverName.Contains(term) ||
                x.CampusName.Contains(term));
        var total = await query.CountAsync(ct);
        var records = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        return ApiResponse<PagedResult<TransportVehicleDriverLinkRowDto>>.SuccessResponse(
            new PagedResult<TransportVehicleDriverLinkRowDto>
            {
                Items = records.Select(x => new TransportVehicleDriverLinkRowDto
                {
                    Id = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                    VehicleId = x.VehicleId, VehicleNumber = x.VehicleNumber,
                    TransportDriverId = x.TransportDriverId, DriverName = x.DriverName,
                    LicenseExpiresOn = x.LicenseExpiresOn, EffectiveFrom = x.EffectiveFrom,
                    EffectiveTo = x.EffectiveTo, IsScheduled = x.IsCurrent && x.EffectiveFrom > today,
                    IsCancelled = !x.IsCurrent && x.EffectiveFrom > today,
                    IsCurrent = x.IsCurrent && x.EffectiveFrom <= today &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today),
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<VehicleDriverAssignmentDto>> AssignDriverAsync(
        AssignVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || request.ClientRequestId == Guid.Empty || request.VehicleId <= 0 ||
            request.TransportDriverId <= 0 ||
            request.EffectiveFrom == default ||
            (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom) ||
            !TryDecodeVersion(request.VehicleRowVersion, out var vehicleVersion) ||
            !TryDecodeVersion(request.DriverRowVersion, out var driverVersion))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Driver, vehicle, request ID, row versions and valid dates are required.");
        var tenant = _currentUser.TenantId;
        const string operation = "transport.vehicle-driver-assign";
        var requestHash = CommandFingerprint(operation, request.VehicleId,
            request.TransportDriverId, request.EffectiveFrom.ToString("yyyy-MM-dd"),
            request.EffectiveTo?.ToString("yyyy-MM-dd"),
            Convert.ToBase64String(vehicleVersion), Convert.ToBase64String(driverVersion));
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.VehicleId && !x.IsDeleted, token);
                var driver = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.TransportDriverId && !x.IsDeleted, token);
                if (vehicle == null || driver == null)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Active vehicle or driver not found.", 404);
                if (!vehicle.CampusId.HasValue)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Vehicle must have an explicit campus.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Campus unavailable.", 409);
                if (!await CanManageCampusAsync(campus.Id, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<VehicleDriverAssignmentDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, vehicle.Id, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (!vehicle.IsActive || !driver.IsActive || vehicle.Capacity < 1)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Active vehicle or driver not found.", 404);
                if (request.EffectiveFrom < today)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment start date has passed.", 409);
                if (!vehicle.RowVersion.AsSpan().SequenceEqual(vehicleVersion) ||
                    !driver.RowVersion.AsSpan().SequenceEqual(driverVersion))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Vehicle or driver changed. Reload.", 409);
                if (!driver.LicenseExpiresOn.HasValue || driver.LicenseExpiresOn.Value < request.EffectiveFrom)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Driver license is not valid for the start date.", 409);
                // End no later than license expiry: never create an assignment valid past the license.
                var end = request.EffectiveTo ?? driver.LicenseExpiresOn.Value;
                if (end > driver.LicenseExpiresOn.Value)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment cannot outlast the driver license.", 409);
                var related = await _driverLinks.GetQueryable().Where(x => x.TenantId == tenant &&
                    !x.IsDeleted && (x.VehicleId == vehicle.Id || x.TransportDriverId == driver.Id))
                    .OrderBy(x => x.Id).ToListAsync(token);
                var exact = related.FirstOrDefault(x => x.IsCurrent && x.VehicleId == vehicle.Id &&
                    x.TransportDriverId == driver.Id && x.EffectiveFrom == request.EffectiveFrom &&
                    x.EffectiveTo == end);
                if (exact != null)
                {
                    var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(exact), "Identical assignment already exists.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        request.ClientRequestId, vehicle.Id, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (related.Any(x => x.IsCurrent && x.EffectiveFrom <= end &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom)))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Vehicle or driver has an overlapping assignment.", 409);
                // Retire expired current rows before insert to respect the existing filtered unique indexes.
                foreach (var old in related.Where(x => x.IsCurrent && x.EffectiveTo.HasValue &&
                    x.EffectiveTo.Value < today))
                {
                    old.IsCurrent = false;
                    old.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    old.UpdatedBy = _currentUser.UserId;
                }
                if (related.Any(x => x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "A current assignment must expire before another can be scheduled.", 409);
                if (related.Any(x => !x.IsCurrent && x.EffectiveTo.HasValue && x.EffectiveTo.Value < today))
                    await _unitOfWork.SaveChangesAsync(token);
                var now = _clock.GetUtcNow().UtcDateTime;
                var entity = new VehicleDriverAssignment
                {
                    TenantId = tenant, VehicleId = vehicle.Id, TransportDriverId = driver.Id,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = end, IsCurrent = true,
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _driverLinks.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync(token);
                var success = new ApiResponse<VehicleDriverAssignmentDto>
                {
                    Success = true, StatusCode = 201, Message = "Driver assigned to vehicle.",
                    Data = MapVehicleDriverLink(entity)
                };
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, vehicle.Id, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver assignment conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver assignment transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Vehicle/driver assignment failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<VehicleDriverAssignmentDto>> CloseDriverAssignmentAsync(
        long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment, end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var link = await _driverLinks.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, token);
                if (link == null) return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted, token);
                if (vehicle?.CampusId == null ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment campus unavailable.", 409);
                if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                if (link.EffectiveTo == request.EffectiveTo)
                    return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Assignment already ends on this date.");
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload.", 409);
                if (!link.IsCurrent || request.EffectiveTo < today || request.EffectiveTo < link.EffectiveFrom ||
                    (link.EffectiveTo.HasValue && request.EffectiveTo > link.EffectiveTo.Value))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Invalid assignment end date.", 409);
                var stranded = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == link.VehicleId &&
                    x.State == TransportAssignmentState.Active &&
                    (!x.EndDate.HasValue || x.EndDate.Value > request.EffectiveTo), token);
                if (stranded)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Close or reschedule active riders before ending the driver assignment.", 409);
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Driver assignment end date saved.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver closure conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver closure transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Vehicle/driver closure failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment could not be closed.", 500);
        }
    }

    private static VehicleDriverAssignmentDto MapVehicleDriverLink(VehicleDriverAssignment x) => new()
    {
        Id = x.Id, VehicleId = x.VehicleId, TransportDriverId = x.TransportDriverId,
        EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, IsCurrent = x.IsCurrent,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
}
