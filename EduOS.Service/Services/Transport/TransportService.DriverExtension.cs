using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanExtendOperatingDriverDates(DateOnly start, DateOnly? currentEnd,
        DateOnly requestedEnd, DateOnly? licenseEnd, DateOnly today) =>
        start <= today && currentEnd.HasValue && currentEnd.Value >= today &&
        requestedEnd > currentEnd.Value && licenseEnd.HasValue && requestedEnd <= licenseEnd.Value;

    /// <summary>Extend the existing operating assignment after a license renewal.
    /// A successful request and its durable receipt commit in the same transaction.</summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> ExtendOperatingDriverAsync(
        long assignmentId, ExtendOperatingVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment, extension date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.operating-driver-extension";
        var requestHash = CommandFingerprint(operation, assignmentId,
            request.EffectiveTo.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        var commandId = request.ClientRequestId != Guid.Empty ? request.ClientRequestId :
            DeriveDriverCommandRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash);
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
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Active vehicle campus unavailable.", 409);
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
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Active licensed driver unavailable.", 409);
                if (link.IsCurrent && link.EffectiveFrom <= today &&
                    link.EffectiveTo == request.EffectiveTo &&
                    driver.LicenseExpiresOn.Value >= request.EffectiveTo)
                {
                    var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Driver assignment already extended to this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        commandId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload.", 409);
                if (!link.IsCurrent || !CanExtendOperatingDriverDates(
                        link.EffectiveFrom, link.EffectiveTo, request.EffectiveTo,
                        driver.LicenseExpiresOn, today))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only a current operating assignment can be extended beyond its existing end, within the renewed license.", 409);
                var currentEnd = link.EffectiveTo!.Value;
                var conflict = await _driverLinks.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent && x.Id != link.Id &&
                    (x.VehicleId == link.VehicleId || x.TransportDriverId == link.TransportDriverId) &&
                    x.EffectiveFrom <= request.EffectiveTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > currentEnd), token);
                if (conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "A vehicle or driver reservation conflicts with the extended dates.", 409);
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Driver assignment extended for tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, end {End}",
                    tenant, vehicle.CampusId.Value, link.Id, link.EffectiveTo);
                var success = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Operating driver assignment extended.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    commandId, assignmentId, requestHash, success, token);
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
            _logger.LogWarning(ex, "Driver extension database conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Driver extension transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Driver extension failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Driver extension failed.", 500);
        }
    }
}
