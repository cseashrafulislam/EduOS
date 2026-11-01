using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

// This first career-network workflow is under the existing ALUMNI entitlement;
// no new plan feature is silently enabled for tenants.
[Authorize(Roles = "TenantAdmin")]
[RequireModule("ALUMNI")]
public sealed class CareerEmployersController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }
}
