using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Post-onboarding management, reusing canonical tenant profile and campus APIs.</summary>
[Authorize(Roles = "TenantAdmin")]
public sealed class InstitutionManagementController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Profile()
    {
        ViewData["InstitutionFeature"] = "profile";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Regional()
    {
        ViewData["InstitutionFeature"] = "regional";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Branding()
    {
        ViewData["InstitutionFeature"] = "branding";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Subdomain()
    {
        ViewData["InstitutionFeature"] = "subdomain";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult History()
    {
        ViewData["InstitutionFeature"] = "history";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Campuses()
    {
        ViewData["InstitutionFeature"] = "campuses";
        return View("Index");
    }
}
