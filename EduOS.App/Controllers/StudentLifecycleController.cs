using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Tenant-admin only student exit, withdrawal and certificate administration.</summary>
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
public sealed class StudentLifecycleController : Controller
{
    private readonly IStudentExitService _exits;
    public StudentLifecycleController(IStudentExitService exits) => _exits = exits;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Certificate(Guid studentReference, string certificateNumber, CancellationToken cancellationToken)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await _exits.GetCertificateAsync(studentReference, certificateNumber, cancellationToken);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode);
        return View(result.Data);
    }
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
