using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize, RequireModule("RESEARCH")]
public sealed class ResearchGrantsController : Controller
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }
}
