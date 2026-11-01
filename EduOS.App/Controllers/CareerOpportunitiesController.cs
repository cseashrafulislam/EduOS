using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Student")]
[RequireModule("ALUMNI")]
public sealed class CareerOpportunitiesController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        ViewData["CareerAdmin"] = User.IsInRole("TenantAdmin");
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }
}
