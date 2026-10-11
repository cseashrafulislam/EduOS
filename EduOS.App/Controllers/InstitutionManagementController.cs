using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Post-onboarding management, reusing canonical tenant profile and campus APIs.</summary>
[Authorize(Roles = "TenantAdmin")]
public sealed class InstitutionManagementController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
