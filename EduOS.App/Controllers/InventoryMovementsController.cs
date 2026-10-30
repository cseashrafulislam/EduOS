using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;
[Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
[RequireModule("INVENTORY")]
public sealed class InventoryMovementsController : Controller
{
    private readonly IInventoryMovementService movements;
    public InventoryMovementsController(IInventoryMovementService movements) => this.movements = movements;

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintReservations(long? locationId, string? search,
        bool includeClosed = false, bool overdueOnly = false, CancellationToken ct = default)
    {
        // A fresh, server-authorized projection; never print cached browser rows.
        var result = await movements.GetReservationRegisterAsync(locationId, search, 1, 250,
            ct, includeClosed, overdueOnly);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode);
        if (result.Data.TotalCount > 250)
            return UnprocessableEntity("Narrow filters to 250 reservation lines or fewer.");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return View(result.Data);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintReservationCoverage(long locationId, string? search,
        bool overdueOnly = false, CancellationToken ct = default)
    {
        var result = await movements.GetReservationCoverageAsync(locationId, search,
            overdueOnly, 1, 250, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode);
        if (result.Data.TotalCount > 250)
            return UnprocessableEntity("Narrow filters to 250 item/lot groups or fewer.");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return View(result.Data);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrintLotAging(long locationId, string? search,
        int minimumDays = 0, CancellationToken ct = default)
    {
        var result = await movements.GetLotAgingAsync(locationId, search, minimumDays, 1, 250, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode);
        if (result.Data.TotalCount > 250)
            return UnprocessableEntity("Narrow filters to 250 item/lot groups or fewer.");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return View(result.Data);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}