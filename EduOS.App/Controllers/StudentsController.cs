using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("STUDENT")]
public sealed class StudentsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    // F10 read-only history workspace: the existing source APIs retain their own authorization.
    [Authorize(Roles = "TenantAdmin")]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Student360(Guid reference)
    {
        if (reference == Guid.Empty) return BadRequest();
        ViewBag.StudentReference = reference.ToString("D");
        return View();
    }

    [Authorize(Roles = "TenantAdmin")]
    [RequireModule("ACADEMIC")]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Create() => View();
}
