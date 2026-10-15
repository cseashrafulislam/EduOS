using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LegalDocumentsController : Controller
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult My() => View();
}
