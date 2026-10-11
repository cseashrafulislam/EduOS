using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Tenant-admin only student exit, withdrawal and certificate administration.</summary>
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
public sealed class StudentLifecycleController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
