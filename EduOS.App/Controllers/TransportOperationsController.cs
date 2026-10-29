using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize]
[RequireModule("TRANSPORT")]
public sealed class TransportOperationsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [Authorize(Roles = "Student,Guardian,Parent")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Family() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult EditStops(Guid reference)
    {
        if (reference == Guid.Empty) return BadRequest();
        ViewBag.RouteReference = reference;
        return View();
    }
}
