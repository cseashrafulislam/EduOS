using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,HR")]
[RequireModule("HR")]
public sealed class HRController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => RedirectToAction(nameof(Directory));

    private IActionResult Feature(string feature)
    {
        ViewData["HrFeature"] = feature;
        return View("Index");
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Directory() => Feature("directory");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Leaves() => Feature("leaves");

    [HttpGet]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ShiftAssignments() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CampusAssignments() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult BankAccounts() => View();
}
