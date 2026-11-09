using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,Teacher")]
[RequireModule("LMS")]
public sealed class TeacherStudioController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Public() => View();

    [HttpGet]
    [Authorize(Roles = "Teacher")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Cpd() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CpdReview() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AdaptiveGoals() => View();
}
