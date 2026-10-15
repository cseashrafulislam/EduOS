using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

// Both modules are independently authorized by their existing API controllers.
[Authorize(Roles = "TenantAdmin,Principal")]
public sealed class OperationsInsightsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
