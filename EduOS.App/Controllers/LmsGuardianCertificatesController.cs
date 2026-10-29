using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "Guardian,Parent")]
[RequireModule("LMS")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsGuardianCertificatesController(ILmsCertificateGuardianService service) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["LmsGuardianMode"] = true;
        return View("~/Views/Learning/Certificates.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> Print([FromQuery] Guid reference, CancellationToken ct)
    {
        if (reference == Guid.Empty) return NotFound();
        var result = await service.GetSharedCertificateAsync(reference, ct);
        if (!result.Success || result.Data == null || result.Data.IsRevoked) return NotFound();
        Response.Headers["Cache-Control"] = "no-store, private";
        return View("~/Views/Learning/CertificatePrint.cshtml", result.Data);
    }
}
