using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanExtendUpcomingDriverDates(DateOnly start, DateOnly? currentEnd,
        DateOnly requestedEnd, DateOnly? licenseEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd > currentEnd.Value && licenseEnd.HasValue &&
        requestedEnd <= licenseEnd.Value;

    /// <summary>
    /// Extend an existing future driver reservation after license renewal.
    /// This preserves its canonical assignment identity, vehicle and driver.
    /// Successful request-key outcomes persist atomically with the canonical assignment.
    /// </summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> ExtendUpcomingDriverAsync(
        long assignmentId, ExtendUpcomingVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment, new end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.upcoming-driver-extension";
        var requestHash = CommandFingerprint(operation, assignmentId,
            request.EffectiveTo.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        var commandId = request.ClientRequestId != Guid.Empty ? request.ClientRequestId :
            LegacyExtensionRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var link = await _driverLinks.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, token);
                if (link == null)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted && x.IsActive, token);
                if (vehicle?.CampusId == null || vehicle.Capacity < 1 ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == vehicle.CampusId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Active vehicle campus unavailable.", 409);
                if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<VehicleDriverAssignmentDto>(tenant,
                    _currentUser.UserId, operation, commandId, assignmentId, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                var driver = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.TransportDriverId && !x.IsDeleted && x.IsActive, token);
                if (driver == null || !driver.LicenseExpiresOn.HasValue)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Active licensed driver unavailable.", 409);
                if (!link.IsCurrent || link.EffectiveFrom <= today)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only a future current driver reservation can be extended.", 409);
                if (link.EffectiveTo == request.EffectiveTo &&
                    driver.LicenseExpiresOn.Value >= request.EffectiveTo)
                {
                    var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Driver reservation already ends on this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        commandId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                if (!CanExtendUpcomingDriverDates(link.EffectiveFrom, link.EffectiveTo,
                    request.EffectiveTo, driver.LicenseExpiresOn, today))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "New end must follow the current end and remain within the driver's renewed license.", 409);
                var oldEnd = link.EffectiveTo!.Value;
                var conflict = await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent && x.Id != link.Id &&
                    (x.VehicleId == link.VehicleId || x.TransportDriverId == link.TransportDriverId) &&
                    x.EffectiveFrom <= request.EffectiveTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > oldEnd), token);
                if (conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Vehicle or driver has an overlapping reservation in the extended period.", 409);
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming driver reservation extended: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, old end {OldEnd}, new end {NewEnd}",
                    tenant, vehicle.CampusId.Value, link.Id, oldEnd, link.EffectiveTo);
                var success = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Upcoming driver reservation extended.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    commandId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming driver extension database conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Upcoming driver extension transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver reservation conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming driver extension failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Driver reservation could not be extended.", 500);
        }
    }
}
