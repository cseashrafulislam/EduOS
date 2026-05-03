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

        if (vm == null)
            return RedirectToAction("Login", "Account");

        return View(vm.Data);
    }
    [HttpGet]
    public IActionResult Admin()
    {
        return View();
    }
}