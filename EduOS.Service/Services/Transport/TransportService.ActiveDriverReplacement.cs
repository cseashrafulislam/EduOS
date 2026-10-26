using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Replace a driver on an already-operating vehicle without interrupting its riders.
    /// Reuses VehicleDriverAssignment history; monetary terms and rider assignments are untouched.
    /// The predecessor/successor and durable request receipt commit in one transaction.
    /// </summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> ReplaceActiveDriverAsync(
        long assignmentId, ReplaceActiveVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.NewDriverId <= 0 || !TryDecodeVersion(request.AssignmentRowVersion, out var sourceVersion) ||
            !TryDecodeVersion(request.DriverRowVersion, out var driverVersion))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment, replacement driver, request ID and row versions are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var yesterday = today.AddDays(-1);
        const string operation = "transport.operating-driver-replacement";
        var requestHash = CommandFingerprint(operation, assignmentId, request.NewDriverId,
            Convert.ToBase64String(sourceVersion), Convert.ToBase64String(driverVersion));
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var source = await _driverLinks.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignmentId, token);
                if (source == null)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.VehicleId && x.IsActive, token);
                if (vehicle == null || vehicle.Capacity < 1 || !vehicle.CampusId.HasValue ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == vehicle.CampusId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Active vehicle campus unavailable.", 409);
                if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                var receipt = await _commandReceipts.ReplayAsync<VehicleDriverAssignmentDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, assignmentId, requestHash, token);
                if (receipt.Conflict)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (receipt.Found) return receipt.Response!;
                var target = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == request.NewDriverId, token);
                if (target == null || !target.LicenseExpiresOn.HasValue || target.LicenseExpiresOn.Value < today)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Replacement driver must have a valid license.", 409);
                if (source.TransportDriverId == target.Id)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Select a different driver.", 409);
                // A successful same-day replay must not mutate history, audit or concurrency tokens.
                if (!source.IsCurrent && source.EffectiveTo == yesterday)
                {
                    var replay = await _driverLinks.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                        x.VehicleId == source.VehicleId && x.TransportDriverId == target.Id &&
                        x.EffectiveFrom == today, token);
                    if (replay != null)
                    {
                        var same = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                            MapVehicleDriverLink(replay), "Replacement already recorded today.");
                        await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                            request.ClientRequestId, assignmentId, requestHash, same, token);
                        await _unitOfWork.SaveChangesAsync(token);
                        return same;
                    }
                }
                if (!source.IsCurrent || source.EffectiveFrom >= today ||
                    (source.EffectiveTo.HasValue && source.EffectiveTo.Value < today))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only a driver assignment operating since before today can be replaced.", 409);
                if (!source.RowVersion.AsSpan().SequenceEqual(sourceVersion) ||
                    !target.RowVersion.AsSpan().SequenceEqual(driverVersion))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment or driver changed. Reload.", 409);
                var related = await _driverLinks.GetQueryable().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted &&
                    (x.VehicleId == source.VehicleId || x.TransportDriverId == target.Id))
                    .OrderBy(x => x.Id).ToListAsync(token);
                if (related.Any(x => x.Id != source.Id && x.IsCurrent &&
                    x.EffectiveFrom <= target.LicenseExpiresOn.Value &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Vehicle or replacement driver already has a current assignment.", 409);
                // Existing filtered unique indexes require retiring old current rows before insertion.
                foreach (var expired in related.Where(x => x.Id != source.Id && x.IsCurrent &&
                    x.EffectiveTo.HasValue && x.EffectiveTo.Value < today))
                {
                    expired.IsCurrent = false;
                    expired.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    expired.UpdatedBy = _currentUser.UserId;
                }
                source.EffectiveTo = yesterday;
                source.IsCurrent = false;
                source.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                source.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                var successor = new VehicleDriverAssignment
                {
                    TenantId = tenant, VehicleId = source.VehicleId, TransportDriverId = target.Id,
                    EffectiveFrom = today, EffectiveTo = target.LicenseExpiresOn.Value,
                    IsCurrent = true, CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _currentUser.UserId
                };
                await _driverLinks.AddAsync(successor);
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Driver replacement tenant {TenantId}, vehicle {VehicleId}, predecessor {OldId}, successor {NewId}",
                    tenant, source.VehicleId, source.Id, successor.Id);
                var success = ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(successor), "Vehicle driver replaced without changing rider assignments.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Driver assignment changed. Reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Driver replacement conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Driver replacement transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent driver assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Driver replacement failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Driver replacement failed.", 500);
        }
    }
}
