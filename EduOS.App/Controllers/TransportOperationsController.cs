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

    private IActionResult TaskPage(string task, string title)
    {
        ViewData["TaskPage"] = task;
        ViewData["TaskTitle"] = title;
        return View("Index");
    }

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Setup() => TaskPage("setup", "Transport setup");

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Assignments() => TaskPage("assignments", "Transport assignments");

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Reports() => TaskPage("reports", "Transport reports");

    [HttpGet]
    [Authorize(Roles = "Student,Guardian,Parent")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Family() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,TransportManager")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Trips() => View();

    [HttpGet]
    [Authorize(Roles = "Staff")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult DriverTrips() => View();

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
