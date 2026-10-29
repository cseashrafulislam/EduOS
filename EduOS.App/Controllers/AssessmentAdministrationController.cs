using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController")]
[RequireModule("EXAM")]
public sealed class AssessmentAdministrationController : Controller
{
    private readonly IAssessmentAdministrationService _assessment;
    public AssessmentAdministrationController(IAssessmentAdministrationService assessment) => _assessment = assessment;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult MarkReviews() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ResultRescrutiny() => View();

    [HttpGet]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ResultFollowUp() => View();

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
    public async Task<IActionResult> TranscriptDocument(Guid reference, CancellationToken ct)
    {
        var result = await _assessment.GetIssuedTranscriptDocumentAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result.Message);
        return View(result.Data);
    }
    
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
