using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanRescheduleUpcomingDriverDates(
        DateOnly originalStart, DateOnly? currentEnd, DateOnly proposedStart,
        DateOnly? licenseEnd, DateOnly today) =>
        originalStart > today && proposedStart > today && proposedStart <= today.AddYears(1) &&
        licenseEnd.HasValue && currentEnd.HasValue &&
        proposedStart <= currentEnd.Value && currentEnd.Value <= licenseEnd.Value;

    /// <summary>
    /// Reschedules the canonical future driver assignment without rewriting rider history.
    /// Persist the successful result with the canonical change; older clients derive a stable key.
    /// </summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> RescheduleUpcomingDriverStartAsync(
        long assignmentId, RescheduleUpcomingVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveFrom == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment, future start date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.upcoming-driver-start-reschedule";
        var requestHash = CommandFingerprint(operation, assignmentId,
            request.EffectiveFrom.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        var commandId = request.ClientRequestId != Guid.Empty ? request.ClientRequestId :
            LegacyDriverCommandRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash);
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
                        "Active vehicle or campus unavailable.", 409);
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
                if (!link.IsCurrent || !CanRescheduleUpcomingDriverDates(
                    link.EffectiveFrom, link.EffectiveTo, request.EffectiveFrom,
                    driver.LicenseExpiresOn, today))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only a future driver assignment can be moved within its end and license expiry.", 409);
                if (link.EffectiveFrom == request.EffectiveFrom)
                {
                    var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Driver assignment already starts on this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        commandId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                var end = link.EffectiveTo ?? DateOnly.MaxValue;
                var conflict = await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent && x.Id != link.Id &&
                    (x.VehicleId == link.VehicleId || x.TransportDriverId == link.TransportDriverId) &&
                    x.EffectiveFrom <= end &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom), token);
                if (conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Vehicle or driver has an overlapping reservation.", 409);
                // Moving later must not leave an existing rider without a scheduled driver.
                if (request.EffectiveFrom > link.EffectiveFrom)
                {
                    var stranded = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.VehicleId == link.VehicleId &&
                        x.State == TransportAssignmentState.Active &&
                        x.StartDate < request.EffectiveFrom &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token);
                    if (stranded)
                        return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                            "Existing riders would lose driver coverage. Reschedule riders first.", 409);
                }
                var oldStart = link.EffectiveFrom;
                link.EffectiveFrom = request.EffectiveFrom;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming driver assignment rescheduled: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, from {OldStart} to {NewStart}",
                    tenant, vehicle.CampusId.Value, link.Id, oldStart, link.EffectiveFrom);
                var success = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Upcoming driver assignment rescheduled.");
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
            _logger.LogWarning(ex, "Driver reschedule database conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Driver reschedule transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Driver reschedule failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Driver assignment could not be rescheduled.", 500);
        }
    }
}
