using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin"), RequireModule("INVENTORY"), RequireModule("FINANCE")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class V3EnterpriseTraceController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
