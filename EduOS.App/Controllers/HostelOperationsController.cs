using EduOS.App.Authorization;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;

namespace EduOS.App.Controllers;

[Authorize]
[RequireModule("HOSTEL")]
public sealed class HostelOperationsController(IHostelService hostelService) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet("hostel/my-history/print")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintMyHistory(
        [FromQuery] int page = 1, [FromQuery] HostelAllocationState? state = null,
        CancellationToken cancellationToken = default)
    {
        var result = await hostelService.GetMyAllocationHistoryAsync(page, 20, state, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        ViewData["CampusScope"] = "My authorized stays";
        ViewData["StateFilter"] = state?.ToString() ?? "All";
        ViewData["DateRange"] = "All";
        ViewData["Search"] = "My stays";
        return View("AllocationHistoryPrint", result.Data);
    }

    [HttpGet("hostel/residents/print")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintResidentRoster(
        [FromQuery] DateOnly operatingDate, [FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var result = await hostelService.GetResidentRosterAsync(
            operatingDate, campusId, page, pageSize, search, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        ViewData["OperatingDate"] = operatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ViewData["CampusScope"] = campusId?.ToString(CultureInfo.InvariantCulture) ?? "All permitted";
        ViewData["Search"] = string.IsNullOrWhiteSpace(search) ? "All" : search.Trim();
        return View("ResidentRosterPrint", result.Data);
    }

    [HttpGet("hostel/allocations/history/print")]
    [Authorize(Roles = "TenantAdmin,Principal,HostelWarden")]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintAllocationHistory(
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] string? search = null, [FromQuery] HostelAllocationState? state = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var result = await hostelService.GetAllocationHistoryAsync(
            campusId, page, pageSize, search, state, fromDate, toDate, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        if (result.Data is null) return StatusCode(500);
        ViewData["CampusScope"] = campusId?.ToString(CultureInfo.InvariantCulture) ?? "All permitted";
        ViewData["StateFilter"] = state?.ToString() ?? "All";
        ViewData["DateRange"] = (fromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Any")
            + " to " + (toDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Any");
        ViewData["Search"] = string.IsNullOrWhiteSpace(search) ? "All" : search.Trim();
        return View("AllocationHistoryPrint", result.Data);
    }
}
