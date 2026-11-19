using EduOS.App.Authorization;
using EduOS.Service.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiWorkspaceController : Controller
{
    [HttpGet]
    [Authorize(Roles = "Teacher,Principal,TenantAdmin")]
    [RequireModule("LMS")]
    public IActionResult Teacher() => View("Index", "Teacher");

    [HttpGet]
    [Authorize(Roles = "Student")]
    [RequireModule("LMS")]
    public IActionResult Tutor() => View("Index", "Tutor");

    [HttpGet]
    [Authorize(Roles = "Principal,TenantAdmin")]
    [RequireModule("ATTENDANCE")]
    public IActionResult Management() => View("Index", "Management");
}
