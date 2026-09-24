using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[AllowAnonymous]
public sealed class PublicAdmissionController : Controller
{
    [HttpGet("apply/{tenantKey}")]
    public IActionResult Index(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey) || tenantKey.Length > 200) return NotFound();
        ViewData["TenantKey"] = tenantKey.Trim();
        return View();
    }
}
