using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Read-only, date-specific pickup/drop planning roster from canonical rider reservations.
    /// No dispatch, attendance, location or finance events are inferred or written.
    /// </summary>
    public async Task<ApiResponse<PagedResult<TransportBoardingManifestRowDto>>> GetBoardingManifestAsync(
        DateOnly operatingDate, long? campusId, Guid? routeReference, Guid? vehicleReference,
        int page, int pageSize, string? search, bool attentionOnly, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportBoardingManifestRowDto>>();
        // The legacy constructor has no driver repositories; never claim a roster is dispatch-ready without them.
        if (_driverLinks is null || _drivers is null || _vehicleCases is null)
            return ApiResponse<PagedResult<TransportBoardingManifestRowDto>>.ErrorResponse(
                "Driver eligibility data is unavailable.", 503);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (operatingDate < today || operatingDate > today.AddDays(90) ||
            campusId is <= 0 || routeReference == Guid.Empty || vehicleReference == Guid.Empty ||
            page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportBoardingManifestRowDto>>.ErrorResponse(
                "Choose an operating date within 90 days and valid filters.");

        var tenant = _currentUser.TenantId;
        var query =
            from assignment in _assignments.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on assignment.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking()
                on assignment.StudentId equals student.Id
            join route in _routes.GetQueryable().AsNoTracking()
                on assignment.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking()
                on assignment.VehicleId equals vehicle.Id
            join campus in _campuses.GetQueryable().AsNoTracking()
                on enrollment.CampusId equals campus.Id
            where assignment.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && route.TenantId == tenant &&
                vehicle.TenantId == tenant && campus.TenantId == tenant &&
                !assignment.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                !campus.IsDeleted && campus.IsActive &&
                enrollment.StudentId == assignment.StudentId &&
                route.CampusId == (long?)campus.Id &&
                vehicle.CampusId == (long?)campus.Id &&
                assignment.State == TransportAssignmentState.Active &&
                assignment.StartDate <= operatingDate &&
                (!assignment.EndDate.HasValue || assignment.EndDate.Value >= operatingDate) &&
                (!campusId.HasValue || campus.Id == campusId.Value) &&
                (!routeReference.HasValue || route.PublicId == routeReference.Value) &&
                (!vehicleReference.HasValue || vehicle.PublicId == vehicleReference.Value)
            select new
            {
                assignment.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                student.StudentCode, RouteName = route.Name, vehicle.VehicleNumber,
                Capacity = vehicle.Capacity,
                OpenVehicleCases = _vehicleCases.GetQueryable().Count(item =>
                    item.TenantId == tenant && !item.IsDeleted && item.VehicleId == vehicle.Id &&
                    item.CampusId == campus.Id && item.State == "Open" &&
                    item.OccurredOn <= operatingDate),
                // Unresolved cases at a former campus hold the physical vehicle.
                // Do not expose former-campus identity or case details.
                CrossCampusCaseHold = _vehicleCases.GetQueryable().Any(item =>
                    item.TenantId == tenant && !item.IsDeleted && item.VehicleId == vehicle.Id &&
                    item.CampusId != campus.Id && item.State == "Open" &&
                    item.OccurredOn <= operatingDate),
                ReservedRiders = _assignments.GetQueryable().Count(rider =>
                    rider.TenantId == tenant && !rider.IsDeleted &&
                    rider.VehicleId == vehicle.Id && rider.State == TransportAssignmentState.Active &&
                    rider.StartDate <= operatingDate &&
                    (!rider.EndDate.HasValue || rider.EndDate.Value >= operatingDate)),
                RouteLinkCount = _vehicleRoutes.GetQueryable().Count(link =>
                    link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                    link.VehicleId == vehicle.Id && link.EffectiveFrom <= operatingDate &&
                    (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= operatingDate) &&
                    _routes.GetQueryable().Any(coveredRoute => coveredRoute.Id == link.RouteId &&
                        coveredRoute.TenantId == tenant && !coveredRoute.IsDeleted &&
                        coveredRoute.IsActive && coveredRoute.CampusId == (long?)campus.Id)),
                DriverLinkCount = _driverLinks.GetQueryable().Count(link =>
                    link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                    link.VehicleId == vehicle.Id && link.EffectiveFrom <= operatingDate &&
                    (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= operatingDate)),
                HasEligibleDriver = _driverLinks.GetQueryable().Any(link =>
                    link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                    link.VehicleId == vehicle.Id && link.EffectiveFrom <= operatingDate &&
                    (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= operatingDate) &&
                    _drivers.GetQueryable().Any(driver => driver.Id == link.TransportDriverId &&
                        driver.TenantId == tenant && !driver.IsDeleted && driver.IsActive &&
                        driver.LicenseExpiresOn.HasValue &&
                        driver.LicenseExpiresOn.Value >= operatingDate)),
                EnrollmentReady = enrollment.IsCurrent &&
                    enrollment.State == EnrollmentState.Active && student.StatusCode == "Active",
                RouteReady = !route.IsDeleted && route.IsActive,
                VehicleReady = !vehicle.IsDeleted && vehicle.IsActive && vehicle.Capacity > 0,
                PickupReady = assignment.PickupStopId.HasValue &&
                    _stops.GetQueryable().Any(stop => stop.TenantId == tenant &&
                        !stop.IsDeleted && stop.IsActive && stop.Id == assignment.PickupStopId.Value &&
                        stop.RouteId == assignment.RouteId),
                DropReady = assignment.DropStopId.HasValue &&
                    _stops.GetQueryable().Any(stop => stop.TenantId == tenant &&
                        !stop.IsDeleted && stop.IsActive && stop.Id == assignment.DropStopId.Value &&
                        stop.RouteId == assignment.RouteId),
                PickupName = _stops.GetQueryable().Where(stop => stop.TenantId == tenant &&
                    !stop.IsDeleted && stop.IsActive && stop.Id == assignment.PickupStopId &&
                    stop.RouteId == assignment.RouteId).Select(stop => stop.Name).FirstOrDefault(),
                DropName = _stops.GetQueryable().Where(stop => stop.TenantId == tenant &&
                    !stop.IsDeleted && stop.IsActive && stop.Id == assignment.DropStopId &&
                    stop.RouteId == assignment.RouteId).Select(stop => stop.Name).FirstOrDefault(),
                HasRouteVehicleLink = _vehicleRoutes.GetQueryable().Any(link =>
                    link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                    link.RouteId == assignment.RouteId && link.VehicleId == assignment.VehicleId &&
                    link.EffectiveFrom <= operatingDate &&
                    (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= operatingDate))
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.StudentCode.Contains(term) ||
                x.RouteName.Contains(term) || x.VehicleNumber.Contains(term));
        if (attentionOnly)
            query = query.Where(x => !x.EnrollmentReady || !x.RouteReady || !x.VehicleReady ||
                !x.PickupReady || !x.DropReady || !x.HasRouteVehicleLink ||
                x.ReservedRiders > x.Capacity || x.RouteLinkCount != 1 ||
                x.DriverLinkCount != 1 || !x.HasEligibleDriver ||
                x.OpenVehicleCases > 0 || x.CrossCampusCaseHold);

        var total = await query.CountAsync(ct);
        var records = await query.OrderBy(x => x.CampusName).ThenBy(x => x.RouteName)
            .ThenBy(x => x.VehicleNumber).ThenBy(x => x.StudentCode).ThenBy(x => x.PublicId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportBoardingManifestRowDto>>.SuccessResponse(
            new PagedResult<TransportBoardingManifestRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = records.Select(x =>
                {
                    var baseReadiness = ClassifyBoardingManifest(x.EnrollmentReady, x.RouteReady,
                        x.VehicleReady, x.PickupReady, x.DropReady, x.HasRouteVehicleLink);
                    var readiness = ClassifyBoardingDispatch(baseReadiness, x.ReservedRiders,
                        x.Capacity, x.RouteLinkCount, x.DriverLinkCount, x.HasEligibleDriver,
                        x.OpenVehicleCases + (x.CrossCampusCaseHold ? 1 : 0));
                    return new TransportBoardingManifestRowDto
                    {
                        AssignmentReference = x.PublicId, CampusId = x.CampusId,
                        CampusName = x.CampusName, StudentCode = x.StudentCode,
                        RouteName = x.RouteName, VehicleNumber = x.VehicleNumber,
                        PickupStop = x.PickupName ?? "Not assigned",
                        DropStop = x.DropName ?? "Not assigned",
                        OperatingDate = operatingDate, Readiness = readiness,
                        Capacity = x.Capacity, ReservedRiders = x.ReservedRiders,
                        OpenVehicleCases = x.OpenVehicleCases,
                        CrossCampusCaseHold = x.CrossCampusCaseHold,
                        RouteLinks = x.RouteLinkCount, DriverLinks = x.DriverLinkCount,
                        DriverEligible = x.DriverLinkCount == 1 && x.HasEligibleDriver,
                        NeedsAttention = readiness != "Ready"
                    };
                }).ToList()
            });
    }

    public static string ClassifyBoardingManifest(bool enrollmentReady, bool routeReady,
        bool vehicleReady, bool pickupReady, bool dropReady, bool hasRouteVehicleLink)
    {
        if (!enrollmentReady) return "EnrollmentReview";
        if (!routeReady) return "RouteReview";
        if (!vehicleReady) return "VehicleReview";
        if (!hasRouteVehicleLink) return "MissingRouteVehicleLink";
        if (!pickupReady) return "PickupReview";
        if (!dropReady) return "DropReview";
        return "Ready";
    }

    /// <summary>Dispatch safety is a derived preview; no trip or attendance is recorded.</summary>
    public static string ClassifyBoardingDispatch(string baseReadiness, int reservedRiders,
        int capacity, int routeLinks, int driverLinks, bool eligibleDriver, int openVehicleCases = 0)
    {
        if (reservedRiders > capacity || capacity <= 0) return "OverCapacity";
        if (baseReadiness != "Ready") return baseReadiness;
        if (openVehicleCases > 0) return "VehicleCaseReview";
        if (routeLinks != 1) return "RouteConflict";
        if (driverLinks == 0) return "NoDriver";
        if (driverLinks > 1) return "DriverConflict";
        if (!eligibleDriver) return "DriverReview";
        return "Ready";
    }

    public async Task<ApiResponse<string>> ExportBoardingManifestCsvAsync(
        DateOnly operatingDate, long? campusId, Guid? routeReference, Guid? vehicleReference,
        string? search, bool attentionOnly, CancellationToken ct = default)
    {
        var first = await GetBoardingManifestAsync(operatingDate, campusId, routeReference,
            vehicleReference, 1, 100, search, attentionOnly, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 500)
            return ApiResponse<string>.ErrorResponse(
                "Manifest export exceeds 500 reservations. Narrow campus, route, vehicle or search.", 413);
        var rows = new List<TransportBoardingManifestRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetBoardingManifestAsync(operatingDate, campusId, routeReference,
                vehicleReference, page, 100, search, attentionOnly, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Manifest changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !seen.Add(x.AssignmentReference)))
            return ApiResponse<string>.ErrorResponse("Manifest changed during export. Retry.", 409);
        var csv = new StringBuilder("Operating Date,Campus,Student Code,Route,Vehicle,Pickup Stop,Drop Stop,Reserved Riders,Capacity,Route Links,Driver Links,Driver Eligible,Open Vehicle Cases,Other-Campus Safety Hold,Readiness\r\n");
        foreach (var row in rows)
            csv.Append(operatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.StudentCode)).Append(',')
                .Append(CsvCell(row.RouteName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(CsvCell(row.PickupStop)).Append(',').Append(CsvCell(row.DropStop)).Append(',')
                .Append(row.ReservedRiders.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RouteLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DriverLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DriverEligible ? "Yes" : "No").Append(',')
                .Append(row.OpenVehicleCases.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CrossCampusCaseHold ? "Yes" : "No").Append(',')
                .Append(CsvCell(row.Readiness)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
