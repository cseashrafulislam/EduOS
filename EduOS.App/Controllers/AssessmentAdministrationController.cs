using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController")]
[RequireModule("EXAM")]
public sealed class AssessmentAdministrationController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult MarkReviews() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult GradeDistribution() => View();
    
    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Transcripts() => View();
    
    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult CertificateTemplates() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult GradeSchemes() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AssessmentSchedules() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AdmitCards() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Certificates() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ComponentMarks() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AssessmentComponents() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult SubjectCompletions() => View();
}
