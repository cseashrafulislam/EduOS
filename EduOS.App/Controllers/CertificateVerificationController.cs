using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CertificateVerificationController : Controller
{
    [HttpGet("/verify/certificate/{reference:guid}")]
    public IActionResult Index(Guid reference)
    {
        if (reference == Guid.Empty) return NotFound();
        ViewData["Reference"] = reference.ToString("D");
        return View();
    }
}
