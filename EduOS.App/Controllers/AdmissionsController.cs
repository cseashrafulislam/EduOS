using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("ADMISSION")]
public class AdmissionsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    // Task-focused pages reuse the existing intake, applicant and review forms/APIs.
    // Index remains available to existing bookmarked workflows.
    [Authorize(Roles = "TenantAdmin")]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Forms()
    {
        ViewData["AdmissionFeature"] = "forms";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult NewApplication()
    {
        ViewData["AdmissionFeature"] = "new";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Applications()
    {
        ViewData["AdmissionFeature"] = "applications";
        return View("Index");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Assessments() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AssessmentSetup()
    {
        ViewData["AdmissionAssessmentFeature"] = "setup";
        return View("Assessments");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Merit()
    {
        ViewData["AdmissionAssessmentFeature"] = "merit";
        return View("Assessments");
    }

    [Authorize(Roles = "TenantAdmin")]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Offers() => View();
}
