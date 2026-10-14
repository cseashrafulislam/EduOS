using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = await _dashboardService.GetDashboardAsync();

        if (vm == null || !vm.Success || vm.Data == null)
        {
            if (vm?.StatusCode == StatusCodes.Status401Unauthorized) return Challenge();
            if (vm?.StatusCode == StatusCodes.Status403Forbidden) return Forbid();
            return StatusCode(vm?.StatusCode is >= 400 and <= 599 ? vm.StatusCode : StatusCodes.Status503ServiceUnavailable);
        }

        return View(vm.Data);
    }
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetPlatformDashboardAsync(cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode is >= 400 and <= 599 ? result.StatusCode : 503);
        Response.Headers.CacheControl = "no-store, private";
        return View(result.Data);
    }
}
