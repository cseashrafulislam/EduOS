using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize, RequireModule("ACCREDITATION")]
public sealed class AccreditationController : Controller
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }


    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Obe()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }
}
