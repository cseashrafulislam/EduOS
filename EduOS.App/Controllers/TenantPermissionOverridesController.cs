using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Post-onboarding member permission exceptions; writes use TenantUserAdministrationService.</summary>
[Authorize(Roles = "TenantAdmin")]
public sealed class TenantPermissionOverridesController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
