using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
public sealed class LearningController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
