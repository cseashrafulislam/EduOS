using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin")]
public sealed class TenantUsersController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Accept() => View();
}
