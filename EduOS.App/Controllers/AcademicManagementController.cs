using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Post-onboarding academic setup. Writes use existing canonical tenant-bound APIs.</summary>
[Authorize(Roles = "TenantAdmin")]
public sealed class AcademicManagementController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
