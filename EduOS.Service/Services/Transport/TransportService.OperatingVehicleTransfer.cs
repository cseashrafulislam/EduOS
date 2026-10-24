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
    public static bool CanTransferOperatingRiderDates(DateOnly start, DateOnly? end, DateOnly effective, DateOnly today) =>
        start <= today && effective >= today && effective > start &&
        (!end.HasValue || effective <= end.Value);

    /// <summary>Campus-scoped, paged options; never exposes student identities or assignments.</summary>
    public async Task<ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>> GetOperatingTransferVehiclesAsync(
        Guid reference, DateOnly effectiveDate, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<UpcomingTransportVehicleOptionDto>>();
        if (reference == Guid.Empty || effectiveDate == default || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Invalid transfer selection.");
        var tenant = _currentUser.TenantId;
        var source = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
        if (source == null) return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Assignment not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentEnrollmentId &&
            x.StudentId == source.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
        if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Active enrollment campus unavailable.", 409);
        if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
            return Denied<PagedResult<UpcomingTransportVehicleOptionDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (source.State != TransportAssignmentState.Active ||
            !CanTransferOperatingRiderDates(source.StartDate, source.EndDate, effectiveDate, today))
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Only operating reservations can be transferred from today onward.", 409);
        if (!await _routes.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == source.RouteId && x.IsActive &&
            (!x.CampusId.HasValue || x.CampusId == enrollment.CampusId), ct))
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Route is unavailable in this campus.", 409);
        var end = source.EndDate ?? DateOnly.MaxValue;
        var options = from link in _vehicleRoutes.GetQueryable().AsNoTracking()
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
            where link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                link.RouteId == source.RouteId && link.EffectiveFrom <= effectiveDate &&
                (!link.EffectiveTo.HasValue || (source.EndDate.HasValue && link.EffectiveTo.Value >= source.EndDate.Value)) &&
                vehicle.TenantId == tenant && !vehicle.IsDeleted && vehicle.IsActive &&
                (!vehicle.CampusId.HasValue || vehicle.CampusId == enrollment.CampusId) &&
                vehicle.Capacity > 0 && vehicle.Id != source.VehicleId
            select new { vehicle.PublicId, VehicleId = vehicle.Id, vehicle.VehicleNumber, vehicle.Capacity };
        // Keep one row per destination vehicle, even if historical route links are duplicated.
        var distinctOptions = options.Distinct();
        var total = await distinctOptions.CountAsync(ct);
        var rows = await distinctOptions.OrderBy(x => x.VehicleNumber).ThenBy(x => x.VehicleId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.VehicleId).ToArray();
        var overlapping = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                ids.Contains(x.VehicleId) && x.StartDate <= end &&
                (!x.EndDate.HasValue || x.EndDate.Value >= effectiveDate))
            .Select(x => new { x.VehicleId, x.StartDate, x.EndDate })
            .Take(10001).ToListAsync(ct);
        if (overlapping.Count > 10000)
            return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.ErrorResponse("Too many reservations to calculate available seats.", 413);
        var peaks = overlapping.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key,
            g => PeakReservedSeats(g.Select(x => (x.StartDate, x.EndDate)), effectiveDate, end).Peak);
        return ApiResponse<PagedResult<UpcomingTransportVehicleOptionDto>>.SuccessResponse(
            new PagedResult<UpcomingTransportVehicleOptionDto>
            {
                Items = rows.Select(x => new UpcomingTransportVehicleOptionDto
                {
                    VehicleReference = x.PublicId, VehicleNumber = x.VehicleNumber,
                    Capacity = x.Capacity, ReservedSeats = peaks.GetValueOrDefault(x.VehicleId),
                    Available = peaks.GetValueOrDefault(x.VehicleId) < x.Capacity, IsCurrent = false
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Preserve historical vehicle and fare by splitting the canonical reservation at the effective date.</summary>
    public async Task<ApiResponse<StudentTransportDto>> TransferOperatingRiderAsync(
        Guid reference, TransferOperatingTransportVehicleRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.VehicleReference == Guid.Empty || request.EffectiveDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Source, destination, effective date, client request ID and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var source = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                if (source == null) return Error("Source reservation not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentEnrollmentId &&
                    x.StudentId == source.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
                    return Error("Active enrollment campus unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<StudentTransportDto>();
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == request.VehicleReference &&
                    x.IsActive && x.Capacity > 0 &&
                    (!x.CampusId.HasValue || x.CampusId == enrollment.CampusId), ct);
                if (vehicle == null || vehicle.Id == source.VehicleId)
                    return Error("Choose a different active vehicle in the enrollment campus.", 409);
                // Durable retry key is unique per tenant on StudentTransport. Check it before row-version validation.
                var previous = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.ClientRequestId == request.ClientRequestId, ct);
                if (previous != null)
                {
                    if (previous.StudentId == source.StudentId &&
                        previous.StudentEnrollmentId == source.StudentEnrollmentId &&
                        previous.RouteId == source.RouteId && previous.VehicleId == vehicle.Id &&
                        previous.StartDate == request.EffectiveDate &&
                        source.EndDate == request.EffectiveDate.AddDays(-1) &&
                        previous.PickupStopId == source.PickupStopId &&
                        previous.DropStopId == source.DropStopId && previous.MonthlyFare == source.MonthlyFare)
                        return ApiResponse<StudentTransportDto>.SuccessResponse(
                            await MapAsync(previous, ct), "Vehicle transfer already processed.");
                    return Error("Client request ID was reused for another reservation.", 409);
                }
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (source.State != TransportAssignmentState.Active ||
                    !CanTransferOperatingRiderDates(source.StartDate, source.EndDate, request.EffectiveDate, today))
                    return Error("Transfer date must be within a currently operating reservation and after its start.", 409);
                if (!source.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Source reservation changed. Reload and retry.", 409);
                if (!await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentId && x.StatusCode == "Active", ct))
                    return Error("Student is unavailable.", 409);
                if (!await _routes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.RouteId && x.IsActive &&
                    (!x.CampusId.HasValue || x.CampusId == enrollment.CampusId), ct))
                    return Error("Source route is unavailable in the enrollment campus.", 409);
                var stopIds = new[] { source.PickupStopId, source.DropStopId }.Where(x => x.HasValue)
                    .Select(x => x!.Value).Distinct().ToArray();
                if (await _stops.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == source.RouteId &&
                    x.IsActive && stopIds.Contains(x.Id), ct) != stopIds.Length)
                    return Error("Existing pickup/drop stops are unavailable on this route.", 409);
                var pairing = await _vehicleRoutes.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                        x.RouteId == source.RouteId && x.VehicleId == vehicle.Id &&
                        x.EffectiveFrom <= request.EffectiveDate &&
                        (!x.EffectiveTo.HasValue ||
                            (source.EndDate.HasValue && x.EffectiveTo.Value >= source.EndDate.Value)))
                    .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
                if (pairing == null) return Error("Destination vehicle is not linked to this route for the remaining period.", 409);
                var end = source.EndDate ?? DateOnly.MaxValue;
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != source.Id &&
                    x.StudentId == source.StudentId && x.State == TransportAssignmentState.Active &&
                    x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveDate), ct))
                    return Error("Student has another reservation during the transfer period.", 409);
                var overlapping = await _assignments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.VehicleId == vehicle.Id && x.State == TransportAssignmentState.Active &&
                        x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveDate))
                    .Select(x => new { x.StartDate, x.EndDate }).Take(10001).ToListAsync(ct);
                if (overlapping.Count > 10000)
                    return Error("Too many overlapping reservations to validate destination capacity.", 413);
                if (PeakReservedSeats(overlapping.Select(x => (x.StartDate, x.EndDate)),
                    request.EffectiveDate, end).Peak >= vehicle.Capacity)
                    return Error("Destination vehicle has no seat available during the transfer period.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var successor = new StudentTransport
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    StudentId = source.StudentId, StudentEnrollmentId = source.StudentEnrollmentId,
                    RouteId = source.RouteId, VehicleId = vehicle.Id, RouteVehicleAssignmentId = pairing.Id,
                    PickupStopId = source.PickupStopId, DropStopId = source.DropStopId,
                    StartDate = request.EffectiveDate, EndDate = source.EndDate,
                    MonthlyFare = source.MonthlyFare, State = TransportAssignmentState.Active,
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                source.EndDate = request.EffectiveDate.AddDays(-1);
                source.UpdatedAt = now;
                source.UpdatedBy = _currentUser.UserId;
                // Save the predecessor first: the tenant/student unique index filters on EndDate IS NULL.
                // Both writes remain inside the same serializable transaction.
                await _unitOfWork.SaveChangesAsync(ct);
                await _assignments.AddAsync(successor);
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Transport operating vehicle transfer: tenant {TenantId}, campus {CampusId}, source {SourceId}, successor {SuccessorId}",
                    tenant, enrollment.CampusId, source.Id, successor.Id);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(successor, ct), "Vehicle transfer saved; prior reservation history retained.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Operating transport vehicle transfer conflict for tenant {TenantId}", tenant);
            return Error("Vehicle transfer conflicts with another update.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Operating transport transfer transaction aborted for tenant {TenantId}", tenant);
            return Error("Vehicle transfer conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operating transport vehicle transfer failed for tenant {TenantId}", tenant);
            return Error("Vehicle transfer could not be saved.", 500);
        }
    }
}
