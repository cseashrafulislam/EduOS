using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> CancelUpcomingDriverAssignmentAsync(
        long assignmentId, CancelUpcomingVehicleDriverAssignmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment and valid row version are required.");
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
                var driver = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.TransportDriverId && !x.IsDeleted, token);
                if (vehicle?.CampusId == null || driver == null ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == vehicle.CampusId.Value && !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment campus unavailable.", 409);
                if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, token))
                    return Denied<VehicleDriverAssignmentDto>();
                if (link.EffectiveFrom <= today)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment has already started.", 409);
                if (!link.IsCurrent)
                    return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Reservation already cancelled.");
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload.", 409);
                var end = link.EffectiveTo ?? DateOnly.MaxValue;
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == link.VehicleId &&
                    x.State == TransportAssignmentState.Active && x.StartDate <= end &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Active rider reservations depend on this vehicle. Reschedule them first.", 409);
                link.IsCurrent = false;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Future driver reservation cancelled.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver cancellation conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Vehicle/driver cancellation aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
    }
}
