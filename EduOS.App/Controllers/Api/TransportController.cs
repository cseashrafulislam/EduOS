using EduOS.App.Authorization;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport")]
[Authorize]
[RequireModule("TRANSPORT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
public sealed class TransportController : ControllerBase
{
    private readonly ITransportService _service;
    public TransportController(ITransportService service) => _service = service;

    [HttpGet("campus-assignment-review")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CampusAssignmentReview(CancellationToken ct) =>
        ToAction(await _service.GetCampusAssignmentReviewAsync(ct));

    [HttpPost("campus-assignment-review/{kind}/{reference:guid}/apply")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ApplyCampusAssignment(string kind, Guid reference,
        [FromBody] ApplyTransportCampusAssignmentDto request, CancellationToken ct) =>
        ToAction(await _service.ApplyCampusAssignmentAsync(kind, reference, request, ct));

    [HttpGet("routes")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Routes(CancellationToken ct) => ToAction(await _service.GetRoutesAsync(ct));

    [HttpGet("vehicles")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Vehicles(CancellationToken ct) => ToAction(await _service.GetVehiclesAsync(ct));

    [HttpGet("eligible-students")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> EligibleStudents([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetEligibleStudentsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("active-assignments")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ActiveAssignments([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetActiveAssignmentsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("assignment-history")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AssignmentHistory([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] TransportAssignmentState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] Guid? routeReference = null, [FromQuery] Guid? vehicleReference = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetAssignmentHistoryAsync(campusId, page, pageSize, search, state,
            fromDate, toDate, routeReference, vehicleReference, cancellationToken));

    [HttpGet("assignment-history/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AssignmentHistoryExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] TransportAssignmentState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] Guid? routeReference = null, [FromQuery] Guid? vehicleReference = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportAssignmentHistoryCsvAsync(campusId, search, state,
            fromDate, toDate, routeReference, vehicleReference, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-assignment-history.csv");
    }

    [HttpGet("my-assignment")]
    public async Task<IActionResult> MyAssignment(CancellationToken ct) => ToAction(await _service.GetMyAssignmentAsync(ct));

    [HttpPost("assignments")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Assign([FromBody] AssignStudentTransportRequestDto request, CancellationToken ct) => ToAction(await _service.AssignAsync(request, ct));

    [HttpPost("assignments/{reference:guid}/extend")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ExtendOperatingRider(Guid reference,
        [FromBody] ExtendOperatingTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendOperatingRiderAsync(reference, request, ct));

    [HttpPost("assignments/{reference:guid}/extend-upcoming")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ExtendUpcomingRider(Guid reference,
        [FromBody] ExtendUpcomingTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendUpcomingRiderAsync(reference, request, ct));

    [HttpPost("assignments/{reference:guid}/shorten-upcoming-no-charge")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ShortenUpcomingNoChargeRider(Guid reference,
        [FromBody] ShortenUpcomingNoChargeTransportRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ShortenUpcomingNoChargeRiderAsync(reference, request, ct));

    [HttpPut("assignments/{reference:guid}/stops")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CorrectUpcomingStops(Guid reference,
        [FromBody] CorrectUpcomingTransportStopsRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CorrectUpcomingStopsAsync(reference, request, ct));

    [HttpPut("assignments/{reference:guid}/active-stops")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CorrectActiveStops(Guid reference,
        [FromBody] CorrectUpcomingTransportStopsRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CorrectActiveStopsAsync(reference, request, ct));

    [HttpGet("assignments/{reference:guid}/vehicle-options")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingVehicleOptions(Guid reference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetUpcomingVehicleOptionsAsync(reference, page, pageSize, ct));

    [HttpGet("assignments/{reference:guid}/transfer-vehicles")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OperatingTransferVehicles(Guid reference, [FromQuery] DateOnly effectiveDate,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetOperatingTransferVehiclesAsync(reference, effectiveDate, page, pageSize, ct));

    [HttpPost("assignments/{reference:guid}/transfer-vehicle")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> TransferOperatingRider(Guid reference,
        [FromBody] TransferOperatingTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.TransferOperatingRiderAsync(reference, request, ct));

    [HttpPost("assignments/{reference:guid}/reschedule-start")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RescheduleUpcomingStart(Guid reference,
        [FromBody] RescheduleUpcomingTransportStartRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RescheduleUpcomingRiderStartAsync(reference, request, ct));

    [HttpPut("assignments/{reference:guid}/vehicle")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ChangeUpcomingVehicle(Guid reference,
        [FromBody] ChangeUpcomingTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ChangeUpcomingVehicleAsync(reference, request, ct));

    [HttpPost("assignments/{reference:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CancelUpcoming(Guid reference,
        [FromBody] CancelUpcomingTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CancelUpcomingAsync(reference, request, ct));

    [HttpPost("assignments/{reference:guid}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Close(Guid reference, [FromBody] CloseStudentTransportRequestDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(reference, request, ct));

    [HttpGet("route-setup/campuses")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteSetupCampuses(CancellationToken ct) =>
        ToAction(await _service.GetRouteSetupCampusesAsync(ct));

    [HttpGet("route-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteSetupPage([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRouteSetupPageAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPost("route-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CreateRouteWithStops([FromBody] CreateTransportRouteRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateRouteWithStopsAsync(request, ct));

    [HttpPut("route-setup/{reference:guid}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> SetRouteState(Guid reference,
        [FromBody] SetTransportRouteStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRouteStateAsync(reference, request, ct));

    [HttpGet("route-setup/{reference:guid}/stops")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteStopsForEdit(Guid reference, CancellationToken ct) =>
        ToAction(await _service.GetRouteStopsForEditAsync(reference, ct));

    [HttpPut("route-setup/{reference:guid}/stops/{stopId:long}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> SetRouteStopState(Guid reference, long stopId,
        [FromBody] SetTransportRouteStopStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRouteStopStateAsync(reference, stopId, request, ct));

    [HttpPut("route-setup/{reference:guid}/stops")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> UpdateRouteStops(Guid reference,
        [FromBody] UpdateTransportRouteStopsRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateRouteStopsAsync(reference, request, ct));

    [HttpPost("route-setup/{reference:guid}/stops")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> AppendRouteStop(Guid reference,
        [FromBody] AppendTransportRouteStopRequestDto request, CancellationToken ct) =>
        ToAction(await _service.AppendRouteStopAsync(reference, request, ct));

    [HttpGet("vehicle-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> VehicleSetupPage([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetVehicleSetupPageAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpGet("vehicle-setup/{reference:guid}/change-impact")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VehicleChangeImpact(Guid reference,
        [FromQuery] int capacity, [FromQuery] bool isActive, [FromQuery] string? rowVersion,
        CancellationToken ct = default) =>
        ToAction(await _service.PreviewVehicleChangeImpactAsync(reference, capacity, isActive, rowVersion, ct));

    [HttpPost("vehicle-setup")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CreateVehicleSetup([FromBody] CreateTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateVehicleSetupAsync(request, ct));

    [HttpPut("vehicle-setup/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> UpdateVehicleSetup(Guid reference, [FromBody] UpdateTransportVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateVehicleSetupAsync(reference, request, ct));

    [HttpGet("route-vehicle-links")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RouteVehicleLinks([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRouteVehicleLinksAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpGet("route-vehicle-links/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RouteVehicleLinksExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportRouteVehicleLinksCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "route-vehicle-history.csv");
    }

    [HttpPost("route-vehicle-links")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> AssignRouteVehicle(
        [FromBody] CreateTransportRouteVehicleLinkRequestDto request, CancellationToken ct) =>
        ToAction(await _service.AssignRouteVehicleAsync(request, ct));

    [HttpGet("route-vehicle-links/{id:long}/replacement-vehicles")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingRouteVehicleChoices(long id,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetUpcomingRouteVehicleChoicesAsync(id, page, pageSize, search, cancellationToken));

    [HttpPost("route-vehicle-links/{id:long}/change-upcoming-vehicle")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ChangeUpcomingRouteVehicle(long id,
        [FromBody] ChangeUpcomingRouteVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ChangeUpcomingRouteVehicleAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/reschedule-start")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> RescheduleUpcomingRouteVehicleStart(long id,
        [FromBody] RescheduleUpcomingRouteVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RescheduleUpcomingRouteVehicleStartAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/extend-upcoming")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ExtendUpcomingRouteVehicle(long id,
        [FromBody] ExtendUpcomingRouteVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendUpcomingRouteVehicleAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/shorten-upcoming")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ShortenUpcomingRouteVehicle(long id,
        [FromBody] ShortenUpcomingRouteVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ShortenUpcomingRouteVehicleAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/extend")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> ExtendOperatingRouteVehicle(long id,
        [FromBody] ExtendOperatingRouteVehicleRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendOperatingRouteVehicleAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CloseRouteVehicle(long id,
        [FromBody] CloseTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CloseRouteVehicleAssignmentAsync(id, request, ct));

    [HttpPost("route-vehicle-links/{id:long}/cancel-upcoming")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CancelUpcomingRouteVehicle(long id, [FromBody] CancelUpcomingRouteVehicleLinkRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CancelUpcomingRouteVehicleAssignmentAsync(id, request, ct));

    [HttpGet("rider-readiness")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RiderReadiness([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = true, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRiderReadinessAsync(campusId, page, pageSize, search, attentionOnly, cancellationToken));

    [HttpGet("rider-readiness/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RiderReadinessExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportRiderReadinessCsvAsync(campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "transport-rider-readiness.csv");
    }

    [HttpGet("seat-forecast")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SeatForecast([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetSeatForecastAsync(campusId, fromDate, toDate, page, pageSize, search, cancellationToken));

    [HttpGet("seat-forecast/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SeatForecastExport([FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, [FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportSeatForecastCsvAsync(campusId, fromDate, toDate, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-seat-forecast.csv");
    }

    [HttpGet("capacity-report")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CapacityReport([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = false, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetCapacityReportAsync(campusId, page, pageSize, search, cancellationToken, attentionOnly));

    [HttpGet("fleet-insights")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FleetInsights([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetFleetInsightsAsync(campusId, search, cancellationToken));

    [HttpGet("fleet-insights/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FleetInsightsExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportFleetInsightsCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "transport-fleet-insights.csv");
    }

    [HttpGet("capacity-report/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> CapacityExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportCapacityCsvAsync(campusId, search, cancellationToken, attentionOnly);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "transport-capacity.csv");
    }


    [HttpGet("driver-registry")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> DriverRegistry([FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetDriversAsync(page, pageSize, search, cancellationToken));

    [HttpPost("driver-registry")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> RegisterDriver([FromBody] SaveTransportDriverRequestDto request,
        CancellationToken ct) => ToAction(await _service.SaveDriverAsync(null, request, ct));

    [HttpGet("driver-registry/{driverId:long}/license-impact")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DriverLicenseImpact(long driverId,
        [FromQuery] DateOnly? proposedExpiry, [FromQuery] bool proposedActive,
        [FromQuery] string? rowVersion, CancellationToken ct) =>
        ToAction(await _service.PreviewDriverLicenseImpactAsync(
            driverId, proposedExpiry, proposedActive, rowVersion, ct));

    [HttpPut("driver-registry/{driverId:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateDriver(long driverId,
        [FromBody] SaveTransportDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SaveDriverAsync(driverId, request, ct));

    [HttpGet("driver-license-compliance")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DriverLicenseCompliance([FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] string? search = null,
        [FromQuery] string? state = "Attention", [FromQuery] int withinDays = 30,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetDriverLicenseComplianceAsync(page, pageSize, search, state, withinDays, cancellationToken));

    [HttpGet("driver-license-compliance/export")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DriverLicenseComplianceExport([FromQuery] string? search = null,
        [FromQuery] string? state = "Attention", [FromQuery] int withinDays = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportDriverLicenseComplianceCsvAsync(search, state, withinDays, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "driver-license-compliance.csv");
    }

    [HttpGet("vehicle-driver-links")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> VehicleDriverLinks([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] string? search = null, [FromQuery] bool scheduledOnly = false,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetVehicleDriverLinksAsync(campusId, page, pageSize, search, cancellationToken, scheduledOnly));

    [HttpGet("vehicle-driver-links/export")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VehicleDriverLinksExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool scheduledOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportVehicleDriverLinksCsvAsync(campusId, search, cancellationToken, scheduledOnly);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8", "vehicle-driver-history.csv");
    }

    [HttpPost("vehicle-driver-links")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> AssignVehicleDriver([FromBody] AssignVehicleDriverRequestDto request,
        CancellationToken ct) => ToAction(await _service.AssignDriverAsync(request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CloseVehicleDriver(long id,
        [FromBody] CloseTransportAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CloseDriverAssignmentAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/replace-active")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ReplaceActiveVehicleDriver(long id,
        [FromBody] ReplaceActiveVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ReplaceActiveDriverAsync(id, request, ct));

    [HttpGet("vehicle-driver-links/{id:long}/replacement-drivers")]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingDriverChoices(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        CancellationToken ct = default) =>
        ToAction(await _service.GetUpcomingDriverChoicesAsync(id, page, pageSize, search, ct));

    [HttpPost("vehicle-driver-links/{id:long}/change-upcoming-driver")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ChangeUpcomingDriver(long id,
        [FromBody] ChangeUpcomingVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ChangeUpcomingDriverAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/reschedule-start")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> RescheduleUpcomingVehicleDriverStart(long id,
        [FromBody] RescheduleUpcomingVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RescheduleUpcomingDriverStartAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/extend-upcoming")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ExtendUpcomingVehicleDriver(long id,
        [FromBody] ExtendUpcomingVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendUpcomingDriverAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/shorten-upcoming")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ShortenUpcomingVehicleDriver(long id,
        [FromBody] ShortenUpcomingVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ShortenUpcomingDriverAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/extend")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ExtendOperatingVehicleDriver(long id,
        [FromBody] ExtendOperatingVehicleDriverRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendOperatingDriverAsync(id, request, ct));

    [HttpPost("vehicle-driver-links/{id:long}/cancel-upcoming")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CancelUpcomingVehicleDriver(long id,
        [FromBody] CancelUpcomingVehicleDriverAssignmentRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CancelUpcomingDriverAssignmentAsync(id, request, ct));

    [HttpGet("rider-turnover")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RiderTurnover([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromMonth = null, [FromQuery] DateOnly? toMonth = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRiderTurnoverAsync(campusId, fromMonth, toMonth, cancellationToken));

    [HttpGet("rider-turnover/export")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RiderTurnoverExport([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromMonth = null, [FromQuery] DateOnly? toMonth = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportRiderTurnoverCsvAsync(campusId, fromMonth, toMonth, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "transport-rider-turnover.csv");
    }

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
