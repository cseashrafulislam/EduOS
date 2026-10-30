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
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian,Student,Teacher,Employee,Staff")]
    public IActionResult Digital() => View();
}