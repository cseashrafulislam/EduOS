using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Post-onboarding academic setup. Writes use existing canonical tenant-bound APIs.</summary>
[Authorize(Roles = "TenantAdmin")]
public sealed class AcademicManagementController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => RedirectToAction(nameof(Years));

    // Dedicated, bookmarkable workspaces; the existing tenant-bound academic APIs remain
    // the single source of truth. The old Index URL redirects to the canonical Years page.
    private IActionResult Feature(string section)
    {
        ViewData["AcademicSection"] = section;
        return View("Index");
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Departments() => Feature("departments");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Years() => Feature("years");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Terms() => Feature("terms");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Classes() => Feature("classes");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Sections() => Feature("sections");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Rooms() => Feature("rooms");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Subjects() => Feature("subjects");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Curricula() => Feature("curricula");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Offerings() => Feature("offerings");

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Prerequisites() => View();
}
