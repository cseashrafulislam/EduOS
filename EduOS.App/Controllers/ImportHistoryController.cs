using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ImportHistoryController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
