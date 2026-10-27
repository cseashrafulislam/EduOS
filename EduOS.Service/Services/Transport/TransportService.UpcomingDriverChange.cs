using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanChangeUpcomingDriverDates(DateOnly start, DateOnly? end,
        DateOnly? licenseEnd, DateOnly today) =>
        start > today && end.HasValue && end.Value >= start &&
        licenseEnd.HasValue && licenseEnd.Value >= end.Value;

    /// <summary>Only eligible tenant drivers, with no phone, license number or employee ID.</summary>
    public async Task<ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>> GetUpcomingDriverChoicesAsync(
        long assignmentId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<UpcomingVehicleDriverChoiceDto>>();
        if (assignmentId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>.ErrorResponse("Invalid driver search.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var link = await _driverLinks.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == assignmentId, ct);
        if (link == null)
            return ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>.ErrorResponse("Assignment not found.", 404);
        var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == link.VehicleId, ct);
        if (vehicle?.CampusId == null || vehicle.Capacity < 1 ||
            !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, ct))
            return ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>.ErrorResponse("Vehicle campus unavailable.", 409);
        if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, ct))
            return Denied<PagedResult<UpcomingVehicleDriverChoiceDto>>();
        if (!link.IsCurrent || !link.EffectiveTo.HasValue || link.EffectiveFrom <= today)
            return ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>.ErrorResponse(
                "Only future driver reservations can change drivers.", 409);
        var end = link.EffectiveTo.Value;
        var query = _drivers.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
            x.Id != link.TransportDriverId && x.LicenseNumber != null && x.LicenseNumber != "" &&
            x.LicenseExpiresOn.HasValue && x.LicenseExpiresOn.Value >= end &&
            !_driverLinks.GetQueryable().Any(a =>
                a.TenantId == tenant && !a.IsDeleted && a.IsCurrent && a.TransportDriverId == x.Id));
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term)) query = query.Where(x => x.FullName.Contains(term));
        var total = await query.CountAsync(ct);
        var choices = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new UpcomingVehicleDriverChoiceDto
            {
                DriverId = x.Id, DriverName = x.FullName, LicenseExpiresOn = x.LicenseExpiresOn!.Value,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<UpcomingVehicleDriverChoiceDto>>.SuccessResponse(
            new PagedResult<UpcomingVehicleDriverChoiceDto>
            {
                Items = choices, TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Change only the planned driver on a not-yet-started canonical assignment.
    /// Exact-state retries and durable request-key outcomes are supported.
    /// The existing filtered unique index permits only one current assignment per driver.
    /// </summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> ChangeUpcomingDriverAsync(
        long assignmentId, ChangeUpcomingVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.NewDriverId <= 0 ||
            !TryDecodeVersion(request.AssignmentRowVersion, out var assignmentVersion) ||
            !TryDecodeVersion(request.DriverRowVersion, out var driverVersion))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment, replacement driver and both row versions are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.upcoming-driver-change";
        var requestHash = CommandFingerprint(operation, assignmentId, request.NewDriverId,
            Convert.ToBase64String(assignmentVersion), Convert.ToBase64String(driverVersion));
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var link = await _driverLinks.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignmentId, token);
                if (link == null)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == link.VehicleId, token);
                if (vehicle?.CampusId == null || vehicle.Capacity < 1 ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Vehicle campus unavailable.", 409);
                if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<VehicleDriverAssignmentDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, assignmentId, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (!link.IsCurrent || !link.EffectiveTo.HasValue || link.EffectiveFrom <= today)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only future driver reservations can change drivers.", 409);
                var driver = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == request.NewDriverId, token);
                if (driver == null || string.IsNullOrWhiteSpace(driver.LicenseNumber) ||
                    !CanChangeUpcomingDriverDates(link.EffectiveFrom, link.EffectiveTo,
                        driver.LicenseExpiresOn, today))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Replacement driver needs a valid license through the reservation end.", 409);
                if (link.TransportDriverId == driver.Id)
                {
                    var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "This driver is already assigned.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        request.ClientRequestId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(assignmentVersion) ||
                    !driver.RowVersion.AsSpan().SequenceEqual(driverVersion))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Assignment or driver changed. Reload and retry.", 409);
                if (await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.Id != link.Id && x.TransportDriverId == driver.Id, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Replacement driver already has a current assignment.", 409);
                var oldDriver = link.TransportDriverId;
                link.TransportDriverId = driver.Id;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming driver changed tenant {TenantId} campus {CampusId} assignment {AssignmentId} old {OldDriver} new {NewDriver}",
                    tenant, vehicle.CampusId.Value, link.Id, oldDriver, driver.Id);
                var success = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Upcoming driver reservation updated.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Driver change database conflict tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Driver change transaction aborted tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver conflict.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Driver change SQL concurrency conflict tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver conflict.", 409);
        }
        catch (InvalidOperationException ex) when (HasSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Driver change SQL serialization conflict tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Driver change failed tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Driver change failed.", 500);
        }
    }
}
