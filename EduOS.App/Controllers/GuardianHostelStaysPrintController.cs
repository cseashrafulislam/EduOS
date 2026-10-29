using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers;

[Authorize(Roles = "Guardian,Parent")]
[RequireModule("HOSTEL")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuardianHostelStaysPrintController(IGuardianHostelStayService service) : Controller
{
    [HttpGet("hostel/guardian-stays/print")]
    public async Task<IActionResult> Index([FromQuery] int page = 1,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await service.GetCurrentStaysAsync(page, 20, search, ct);
        if (!result.Success || result.Data is null)
            return StatusCode(result.StatusCode, result);
        return View(result.Data);
    }
}
