using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

/// <summary>Advisory preview only; UpdateVehicleSetupAsync repeats all guards in its serializable transaction.</summary>
public sealed partial class TransportService
{
    public static bool CanApplyVehicleSettings(int proposedCapacity, bool proposedActive,
        int peakReservedSeats, int activeReservationRecords, int currentRoutes, int currentDrivers) =>
        proposedCapacity is >= 1 and <= 1000 && proposedCapacity >= peakReservedSeats &&
        (proposedActive || (activeReservationRecords == 0 && currentRoutes == 0 && currentDrivers == 0));

    public async Task<ApiResponse<TransportVehicleChangeImpactDto>> PreviewVehicleChangeImpactAsync(
        Guid reference, int proposedCapacity, bool proposedActive, string? rowVersion,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportVehicleChangeImpactDto>();
        if (reference == Guid.Empty || proposedCapacity is < 1 or > 1000 ||
            !TryDecodeVersion(rowVersion, out var version))
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse(
                "Vehicle, proposed capacity and current row version are required.");

        var tenant = _currentUser.TenantId;
        var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
        if (vehicle == null)
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse("Vehicle not found.", 404);
        if (!vehicle.CampusId.HasValue)
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse(
                "Vehicle has no assigned campus. Platform review is required.", 409);
        if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == vehicle.CampusId.Value && x.IsActive && !x.IsDeleted, ct))
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse("Active campus not found.", 404);
        if (!await CanManageCampusAsync(vehicle.CampusId.Value, tenant, ct))
            return Denied<TransportVehicleChangeImpactDto>();
        if (!vehicle.RowVersion.AsSpan().SequenceEqual(version))
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse(
                "Vehicle changed. Reload before checking impact.", 409);

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        // Match the write-side guard exactly: all active reservation records block deactivation,
        // while only concurrent reservations from today forward determine minimum capacity.
        var bookings = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                x.State == TransportAssignmentState.Active)
            .Select(x => new { x.StartDate, x.EndDate }).Take(10001).ToListAsync(ct);
        if (bookings.Count > 10000)
            return ApiResponse<TransportVehicleChangeImpactDto>.ErrorResponse(
                "Too many reservations to preview vehicle capacity. Narrow the data first.", 413);
        var occupiedToday = bookings.Count(x => x.StartDate <= today &&
            (!x.EndDate.HasValue || x.EndDate.Value >= today));
        var peak = PeakReservedSeats(bookings.Select(x => (x.StartDate, x.EndDate)),
            today, DateOnly.MaxValue).Peak;
        var routes = await _vehicleRoutes.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
            x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct);
        var drivers = _driverLinks == null ? 0 : await _driverLinks.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
            x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct);
        var allowed = CanApplyVehicleSettings(proposedCapacity, proposedActive, peak, bookings.Count, routes, drivers);
        return ApiResponse<TransportVehicleChangeImpactDto>.SuccessResponse(new TransportVehicleChangeImpactDto
        {
            Reference = vehicle.PublicId,
            ProposedCapacity = proposedCapacity,
            ProposedActive = proposedActive,
            ActiveReservationRecords = bookings.Count,
            OccupiedToday = occupiedToday,
            PeakReservedSeats = peak,
            CurrentRouteAssignments = routes,
            CurrentDriverAssignments = drivers,
            CanApply = allowed,
            Explanation = proposedCapacity < peak
                ? "Capacity cannot be below peak concurrent reservations."
                : !proposedActive && (bookings.Count > 0 || routes > 0 || drivers > 0)
                    ? "Close rider, route and driver assignments before deactivating."
                    : "No current reservation or assignment conflict detected. Save rechecks under transaction."
        });
    }
}
