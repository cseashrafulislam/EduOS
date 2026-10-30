using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
public sealed class LearningController : Controller
{
    private readonly ILmsWorkflowService _lms;
    public LearningController(ILmsWorkflowService lms) => _lms = lms;
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Quizzes() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult LiveClasses() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CourseContent() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AssignmentFiles() => View();
    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CourseCompletions() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Certificates() => View();
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CertificatePrint([FromQuery] Guid reference, CancellationToken ct)
    {
        if (reference == Guid.Empty) return NotFound();
        // A fresh authenticated server read, not browser-held certificate state, controls printing.
        var result = await _lms.GetCertificateAsync(reference, ct);
        if (!result.Success || result.Data == null || result.Data.IsRevoked)
            return NotFound();
        Response.Headers["Cache-Control"] = "no-store, private";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'self'; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        return View(result.Data);
    }

    [HttpGet]
    [Authorize(Roles = "Student")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult MySubmissions() => View();
}
