using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanShortenUpcomingDriverDates(
        DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd >= start && requestedEnd < currentEnd.Value;

    /// <summary>Shorten a future driver reservation while protecting active rider coverage.
    /// Exact-state replay is safe; durable request-key idempotency is Platform-owned.</summary>
    public async Task<ApiResponse<VehicleDriverAssignmentDto>> ShortenUpcomingDriverAsync(
        long assignmentId, ShortenUpcomingVehicleDriverRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<VehicleDriverAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment, new end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
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
                var driver = await _drivers.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.TransportDriverId && !x.IsDeleted && x.IsActive, token);
                if (driver == null || !driver.LicenseExpiresOn.HasValue)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Active licensed driver unavailable.", 409);
                if (!link.IsCurrent || link.EffectiveFrom <= today || !link.EffectiveTo.HasValue)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Only a future finite current driver reservation can be shortened.", 409);
                if (link.EffectiveTo.Value == request.EffectiveTo &&
                    driver.LicenseExpiresOn.Value >= request.EffectiveTo)
                    return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                        MapVehicleDriverLink(link), "Driver reservation already ends on this date.");
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                if (!CanShortenUpcomingDriverDates(
                    link.EffectiveFrom, link.EffectiveTo, request.EffectiveTo, today) ||
                    driver.LicenseExpiresOn.Value < request.EffectiveTo)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "New end must be within the future reservation and driver's license validity.", 409);
                var oldEnd = link.EffectiveTo.Value;
                var affectedRiders = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted &&
                    x.State == TransportAssignmentState.Active &&
                    x.VehicleId == link.VehicleId && x.StartDate <= oldEnd &&
                    (!x.EndDate.HasValue || x.EndDate.Value > request.EffectiveTo), token);
                if (affectedRiders)
                    return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                        "Active riders require the days being removed. Adjust rider reservations first.", 409);
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming driver reservation shortened: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, old end {OldEnd}, new end {NewEnd}",
                    tenant, vehicle.CampusId.Value, link.Id, oldEnd, link.EffectiveTo);
                return ApiResponse<VehicleDriverAssignmentDto>.SuccessResponse(
                    MapVehicleDriverLink(link), "Upcoming driver reservation shortened.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming driver shortening database conflict for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Upcoming driver shortening transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Concurrent driver reservation conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming driver shortening failed for tenant {TenantId}", tenant);
            return ApiResponse<VehicleDriverAssignmentDto>.ErrorResponse(
                "Driver reservation could not be shortened.", 500);
        }
    }
}
