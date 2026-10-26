using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanShortenUpcomingNoChargeRiderDates(
        DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd >= start && requestedEnd < currentEnd.Value;

    public static bool IsNoChargeRiderFare(decimal monthlyFare) => monthlyFare == 0m;

    /// <summary>Shorten only future zero-fare reservations; charged changes belong to Finance.</summary>
    public async Task<ApiResponse<StudentTransportDto>> ShortenUpcomingNoChargeRiderAsync(
        Guid reference, ShortenUpcomingNoChargeTransportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null || request.EndDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment, end date and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var assignment = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                if (assignment == null) return Error("Assignment not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
                    x.StudentId == assignment.StudentId && x.IsCurrent &&
                    x.State == EnrollmentState.Active, ct);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
                    return Error("Active enrollment campus unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<StudentTransportDto>();
                if (assignment.State != TransportAssignmentState.Active || !assignment.EndDate.HasValue)
                    return Error("Only active fixed-term reservations can be shortened.", 409);
                if (!IsNoChargeRiderFare(assignment.MonthlyFare))
                    return Error("Charged reservations require Finance billing integration.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (assignment.StartDate <= today || assignment.EndDate.Value < assignment.StartDate)
                    return Error("Only valid unstarted reservations can be shortened.", 409);
                // Exact-state replay is harmless even if the caller holds the old token.
                if (request.EndDate == assignment.EndDate.Value)
                    return ApiResponse<StudentTransportDto>.SuccessResponse(
                        await MapAsync(assignment, ct), "Reservation end date already matches.");
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    assignment.StudentId, assignment.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before shortening.", 409);
                if (!CanShortenUpcomingNoChargeRiderDates(
                    assignment.StartDate, assignment.EndDate, request.EndDate, today))
                    return Error("New end date must be within the original reservation.", 409);
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId && x.IsActive, ct);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.VehicleId && x.IsActive, ct);
                if (route == null || vehicle == null || vehicle.Capacity <= 0 ||
                    (route.CampusId.HasValue && route.CampusId.Value != enrollment.CampusId) ||
                    (vehicle.CampusId.HasValue && vehicle.CampusId.Value != enrollment.CampusId))
                    return Error("Active route or vehicle is unavailable for this campus.", 409);
                if (!await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentId &&
                    x.StatusCode == "Active", ct))
                    return Error("Student is no longer active.", 409);
                if (!await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.RouteId == route.Id && x.VehicleId == vehicle.Id &&
                    (!assignment.RouteVehicleAssignmentId.HasValue ||
                        x.Id == assignment.RouteVehicleAssignmentId.Value) &&
                    x.EffectiveFrom <= assignment.StartDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EndDate), ct))
                    return Error("Route-vehicle assignment does not cover the new period.", 409);
                // A shorter interval cannot create a new capacity or overlap conflict.
                var oldEnd = assignment.EndDate.Value;
                assignment.EndDate = request.EndDate;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Upcoming no-charge transport reservation shortened: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, {OldEnd} to {NewEnd}",
                    tenant, enrollment.CampusId, assignment.Id, oldEnd, request.EndDate);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, ct), "Upcoming no-charge reservation shortened.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Assignment changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport no-charge shortening conflict for tenant {TenantId}", tenant);
            return Error("Reservation conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Transport shortening transaction aborted for tenant {TenantId}", tenant);
            return Error("Concurrent reservation change. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport shortening failed for tenant {TenantId}", tenant);
            return Error("Reservation end date could not be shortened.", 500);
        }
    }
}
