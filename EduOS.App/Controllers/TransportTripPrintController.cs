using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Authorized, server-rendered full manifest; not the paginated screen preview.</summary>
[Authorize]
[RequireModule("TRANSPORT")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportTripPrintController : Controller
{
    private readonly ITransportTripService _service;
    public TransportTripPrintController(ITransportTripService service) => _service = service;

    [HttpGet("/transport/trips/{reference:guid}/print")]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    public async Task<IActionResult> Dispatcher(Guid reference, CancellationToken ct)
    {
        var result = await _service.GetManifestReportAsync(reference, ct);
        return RenderReport(result);
    }

    [HttpGet("/transport/driver-trips/{reference:guid}/print")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Driver(Guid reference, CancellationToken ct)
    {
        var result = await _service.GetDriverManifestReportAsync(reference, ct);
        return RenderReport(result);
    }

    private IActionResult RenderReport(ApiResponse<TransportTripManifestReportDto> result)
    {
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View("~/Views/TransportOperations/TripManifestPrint.cshtml", result.Data);
    }
}
