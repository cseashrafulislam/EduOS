using EduOS.App.Authorization;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hostel")]
[Authorize]
[RequireModule("HOSTEL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HostelController : ControllerBase
{
    private readonly IHostelService _service;
    public HostelController(IHostelService service) => _service = service;

    [HttpGet("applications/authority/guardians")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> AuthorityGuardianOptions([FromQuery] Guid enrollmentReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetAuthorityGuardianOptionsAsync(enrollmentReference, page, pageSize, ct));

    [HttpGet("applications/authority")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ApplicationAuthorities([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetApplicationAuthoritiesAsync(campusId, page, pageSize, ct));

    [HttpPost("applications/authority")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> GrantApplicationAuthority(
        [FromBody] GrantHostelApplicationAuthorityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.GrantApplicationAuthorityAsync(request, ct));

    [HttpPost("applications/authority/{id:long}/revoke")]
    [Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> RevokeApplicationAuthority(long id,
        [FromBody] RevokeHostelApplicationAuthorityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RevokeApplicationAuthorityAsync(id, request, ct));

    [HttpGet("applications/options")]
    public async Task<IActionResult> MyApplicationOptions([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetMyApplicationOptionsAsync(page, pageSize, ct));

    [HttpPost("applications")]
    public async Task<IActionResult> SubmitApplication([FromBody] SubmitHostelApplicationRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SubmitApplicationAsync(request, ct));

    [HttpGet("applications/my")]
    public async Task<IActionResult> MyApplications([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => ToAction(await _service.GetMyApplicationsAsync(page, pageSize, ct));

    [HttpGet("applications/queue")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ApplicationQueue([FromQuery] long? campusId = null,
        [FromQuery] HostelApplicationState? state = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken ct = default) =>
        ToAction(await _service.GetApplicationQueueAsync(campusId, state, page, pageSize, search, ct));

    [HttpGet("applications/queue/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ApplicationQueueExport([FromQuery] long? campusId = null,
        [FromQuery] HostelApplicationState? state = null, [FromQuery] int page = 1,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _service.ExportApplicationQueueCsvAsync(campusId, state, search, page, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-applications-page.csv");
    }

    [HttpGet("applications/{id:long}/events")]
    public async Task<IActionResult> ApplicationEvents(long id, CancellationToken ct) =>
        ToAction(await _service.GetApplicationEventsAsync(id, ct));

    [HttpPost("applications/{id:long}/{decision:regex(^(waitlist|offer|reject|withdraw)$)}")]
    public async Task<IActionResult> TransitionApplication(long id, string decision,
        [FromBody] HostelApplicationDecisionRequestDto request, CancellationToken ct) =>
        ToAction(await _service.TransitionApplicationAsync(id, decision, request, ct));

    [HttpPost("applications/{id:long}/accept")]
    public async Task<IActionResult> AcceptApplication(long id,
        [FromBody] HostelApplicationDecisionRequestDto request, CancellationToken ct) =>
        ToAction(await _service.AcceptApplicationOfferAsync(id, request, ct));

    [HttpGet("facility-campuses")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FacilityCampuses(CancellationToken ct) =>
        ToAction(await _service.GetFacilityCampusesAsync(ct));

    [HttpGet("facilities")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Facilities([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken ct = default) =>
        ToAction(await _service.GetFacilitiesAsync(campusId, page, pageSize, search, ct));

    [HttpGet("facilities/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FacilitiesExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _service.ExportFacilitiesCsvAsync(campusId, search, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-facilities.csv");
    }

    [HttpPost("facilities")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateFacility([FromBody] CreateHostelFacilityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateFacilityAsync(request, ct));

    [HttpPut("facilities/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> UpdateFacility(Guid reference, [FromBody] UpdateHostelFacilityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateFacilityAsync(reference, request, ct));

    [HttpPut("facilities/{reference:guid}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetFacilityState(Guid reference, [FromBody] SetHostelFacilityStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetFacilityStateAsync(reference, request, ct));

    [HttpPost("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateHostelRoomRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateRoomAsync(request, ct));

    [HttpGet("room-inventory")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RoomInventory([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRoomInventoryAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpGet("room-inventory/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RoomInventoryExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportRoomInventoryCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-room-inventory.csv");
    }

    [HttpPut("rooms/{id:long}/number")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> RenameRoom(long id, [FromBody] RenameHostelRoomRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RenameRoomAsync(id, request, ct));

    [HttpPut("rooms/{id:long}/capacity")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetRoomCapacity(long id, [FromBody] SetHostelRoomCapacityRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRoomCapacityAsync(id, request, ct));

    [HttpPut("rooms/{id:long}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetRoomState(long id, [FromBody] SetHostelRoomStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetRoomStateAsync(id, request, ct));

    [HttpPost("beds")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CreateBed([FromBody] CreateHostelBedRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateBedAsync(request, ct));

    [HttpGet("beds/{id:long}/relocation-rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedRelocationRooms(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken ct = default) =>
        ToAction(await _service.GetBedRelocationRoomsAsync(id, page, pageSize, search, ct));

    [HttpPut("beds/{id:long}/relocate")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> RelocateUnusedBed(long id,
        [FromBody] RelocateUnusedHostelBedRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RelocateUnusedBedAsync(id, request, ct));

    [HttpGet("bed-availability")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedAvailability([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetBedAvailabilityAsync(campusId, fromDate ?? default,
            toDate ?? default, page, pageSize, search, cancellationToken));

    [HttpGet("bed-availability/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedAvailabilityExport([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportBedAvailabilityCsvAsync(campusId, fromDate ?? default,
            toDate ?? default, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-bed-availability.csv");
    }

    [HttpGet("bed-inventory")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedInventory([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetBedInventoryAsync(campusId, page, pageSize, search, cancellationToken));

    [HttpPut("beds/{id:long}/number")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CorrectUnusedBedNumber(long id,
        [FromBody] CorrectUnusedHostelBedNumberRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CorrectUnusedBedNumberAsync(id, request, ct));

    [HttpPut("beds/{id:long}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> SetBedState(long id, [FromBody] SetHostelBedStateRequestDto request, CancellationToken ct) =>
        ToAction(await _service.SetBedStateAsync(id, request, ct));

    [HttpGet("bed-inventory/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BedInventoryExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportBedInventoryCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-bed-inventory.csv");
    }

    [HttpGet("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Rooms(CancellationToken ct) => ToAction(await _service.GetRoomsAsync(ct));

    [HttpGet("capacity")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Capacity([FromQuery] long? campusId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] bool attentionOnly = false, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetCapacityAsync(campusId, page, pageSize, search, cancellationToken, attentionOnly));

    [HttpGet("turnover-report")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TurnoverReport([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromMonth = null, [FromQuery] DateOnly? toMonth = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetTurnoverReportAsync(campusId, fromMonth, toMonth, cancellationToken));

    [HttpGet("turnover-report/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TurnoverReportExport([FromQuery] long? campusId = null,
        [FromQuery] DateOnly? fromMonth = null, [FromQuery] DateOnly? toMonth = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportTurnoverReportCsvAsync(campusId, fromMonth, toMonth, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-turnover-report.csv");
    }

    [HttpGet("occupancy-insights")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OccupancyInsights([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetOccupancyInsightsAsync(campusId, search, cancellationToken));

    [HttpGet("occupancy-insights/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OccupancyInsightsExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportOccupancyInsightsCsvAsync(campusId, search, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-occupancy-insights.csv");
    }

    [HttpGet("capacity/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportCapacity([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] bool attentionOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportCapacityCsvAsync(campusId, search, cancellationToken, attentionOnly);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-capacity.csv");
    }

    [HttpGet("eligible-students")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> EligibleStudents([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetEligibleStudentsAsync(page, pageSize, search, cancellationToken, startDate, endDate));

    [HttpGet("available-beds")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> AvailableBeds([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] DateOnly? startDate = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetAvailableBedsAsync(page, pageSize, search, cancellationToken, startDate));

    [HttpGet("active-allocations")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ActiveAllocations([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetActiveAllocationsAsync(page, pageSize, search, cancellationToken));

    [HttpGet("checkout-follow-ups")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckoutFollowUps([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] int daysAhead = 30,
        [FromQuery] bool overdueOnly = false, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetCheckoutFollowUpsAsync(campusId, page, pageSize, search,
            daysAhead, overdueOnly, cancellationToken));

    [HttpGet("checkout-follow-ups/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckoutFollowUpsExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null, [FromQuery] int daysAhead = 30,
        [FromQuery] bool overdueOnly = false, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportCheckoutFollowUpsCsvAsync(campusId, search,
            daysAhead, overdueOnly, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-checkout-follow-ups.csv");
    }

    [HttpGet("resident-roster")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResidentRoster([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        CancellationToken ct = default) =>
        ToAction(await _service.GetResidentRosterAsync(operatingDate, campusId, page, pageSize, search, ct));

    [HttpGet("resident-roster/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResidentRosterExport([FromQuery] DateOnly operatingDate,
        [FromQuery] long? campusId = null, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await _service.ExportResidentRosterCsvAsync(operatingDate, campusId, search, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-resident-roster.csv");
    }

    [HttpGet("allocation-history")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AllocationHistory([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] EduOS.Core.Enums.Domain.HostelAllocationState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetAllocationHistoryAsync(campusId, page, pageSize, search, state, fromDate, toDate, cancellationToken));

    [HttpGet("allocation-history/export")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AllocationHistoryExport([FromQuery] long? campusId = null,
        [FromQuery] string? search = null,
        [FromQuery] EduOS.Core.Enums.Domain.HostelAllocationState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExportAllocationHistoryCsvAsync(campusId, search, state,
            fromDate, toDate, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "hostel-allocation-history.csv");
    }

    [HttpGet("my-allocation")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> MyAllocation(CancellationToken ct) => ToAction(await _service.GetMyAllocationAsync(ct));

    [HttpGet("my-history")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> MyHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] EduOS.Core.Enums.Domain.HostelAllocationState? state = null,
        CancellationToken ct = default) =>
        ToAction(await _service.GetMyAllocationHistoryAsync(page, pageSize, state, ct));

    [HttpGet("my-history/export")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> MyHistoryExport([FromQuery] int page = 1,
        [FromQuery] EduOS.Core.Enums.Domain.HostelAllocationState? state = null,
        CancellationToken ct = default)
    {
        var result = await _service.ExportMyAllocationHistoryPageCsvAsync(page, state, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes(result.Data)).ToArray(),
            "text/csv; charset=utf-8", "my-hostel-stays.csv");
    }

    [HttpPost("allocations")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Allocate([FromBody] AllocateStudentHostelRequestDto request, CancellationToken ct) => ToAction(await _service.AllocateAsync(request, ct));

    [HttpPost("allocations/{id:long}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> Close(long id, [FromBody] CloseStudentHostelAllocationRequestDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(id, request, ct));

    [HttpGet("allocations/{id:long}/occupied-transfer-beds")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> OccupiedTransferBeds(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetOccupiedTransferBedOptionsAsync(id, page, pageSize, ct));

    [HttpPost("allocations/{id:long}/occupied-transfer")]
    [HttpPost("allocations/{id:long}/occupied-transfer-no-charge")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> TransferOccupiedNoCharge(long id,
        [FromBody] TransferOccupiedHostelRequestDto request, CancellationToken ct) =>
        ToAction(await _service.TransferOccupiedNoChargeAsync(id, request, ct));

    [HttpPost("allocations/{id:long}/cancel")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> CancelUpcomingAllocation(long id,
        [FromBody] CancelStudentHostelAllocationRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CancelUpcomingAllocationAsync(id, request, ct));

    [HttpPost("allocations/{id:long}/extend-no-charge")]
    [HttpPost("allocations/{id:long}/extend-stay")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ExtendNoChargeStay(long id,
        [FromBody] ExtendNoChargeHostelStayRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ExtendNoChargeStayAsync(id, request, ct));

    [HttpPost("allocations/{id:long}/shorten-upcoming-no-charge")]
    [HttpPost("allocations/{id:long}/shorten-upcoming")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ShortenUpcomingNoChargeStay(long id,
        [FromBody] ShortenUpcomingNoChargeHostelStayRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ShortenUpcomingNoChargeStayAsync(id, request, ct));

    [HttpPost("allocations/{id:long}/reschedule-start")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> RescheduleUpcomingAllocationStart(long id,
        [FromBody] RescheduleUpcomingHostelStartRequestDto request, CancellationToken ct) =>
        ToAction(await _service.RescheduleUpcomingAllocationStartAsync(id, request, ct));

    [HttpGet("allocations/{id:long}/bed-options")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingBedOptions(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetUpcomingBedReassignmentOptionsAsync(id, page, pageSize, ct));

    [HttpPost("allocations/{id:long}/reassign-bed")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ReassignUpcomingBed(long id,
        [FromBody] ReassignUpcomingHostelBedRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ReassignUpcomingBedAsync(id, request, ct));

    [HttpGet("allocations/{id:long}/room-options")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingRoomOptions(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        ToAction(await _service.GetUpcomingRoomTransferOptionsAsync(id, page, pageSize, ct));

    [HttpPost("allocations/{id:long}/reassign-room")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    public async Task<IActionResult> ReassignUpcomingRoom(long id,
        [FromBody] ReassignUpcomingHostelRoomRequestDto request, CancellationToken ct) =>
        ToAction(await _service.ReassignUpcomingRoomAsync(id, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
