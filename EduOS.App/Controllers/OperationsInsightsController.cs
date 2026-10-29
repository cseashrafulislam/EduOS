using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace EduOS.App.Controllers;

// The two operational APIs remain independently authorized. The overview itself
// must also deny direct navigation when neither module is available to this tenant.
[Authorize(Roles = "TenantAdmin,Principal")]
public sealed class OperationsInsightsController(
    ITenantModuleService tenantModuleService, IConfiguration configuration) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index()
    {
        var available = await OptionalModuleNavigation.GetAvailableAsync(
            tenantModuleService, configuration.GetValue<bool>("PilotRelease:Enabled"),
            User?.Identity?.IsAuthenticated == true && User?.IsInRole("SuperAdmin") != true);
        var transport = available.Contains("TRANSPORT");
        var hostel = available.Contains("HOSTEL");
        if (!transport && !hostel) return Forbid();

        ViewData["ShowTransport"] = transport;
        ViewData["ShowHostel"] = hostel;
        return View();
    }
}
