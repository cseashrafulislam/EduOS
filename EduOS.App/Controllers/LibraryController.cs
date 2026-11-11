using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;
[Authorize]
[RequireModule("LIBRARY")]
public sealed class LibraryController : Controller
{
    [HttpGet]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Branches() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Reservations() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Circulation() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Overdue() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Integrity() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Conditions() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Liabilities() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent")]
    public IActionResult Digital() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent")]
    public IActionResult OfficialBooks() => View("~/Views/Shared/OfficialEducationBooks.cshtml");

    // Legacy LMS bookmark: canonical Library authorization and entitlement apply
    // before redirecting. Do not render a second catalogue under Learning.
    [HttpGet("/Learning/OfficialBooks")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent")]
    public IActionResult LegacyLearningOfficialBooks() => RedirectToAction(nameof(OfficialBooks), "Library");

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff,Guardian,Parent")]
    public IActionResult Study() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Student,Teacher")]
    public IActionResult StudyAssistant() => View();
}
