using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ACADEMIC")]
public sealed class AcademicRoutinesController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
