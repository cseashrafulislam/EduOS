using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "Guardian,Parent")]
[RequireModule("LMS")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsGuardianProgressController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
