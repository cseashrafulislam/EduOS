using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,Accountant")]
[RequireModule("FINANCE")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeeDocumentsController : Controller
{
    [HttpGet]
    public IActionResult Invoice()
    {
        ViewData["DocumentKind"] = "invoice";
        return View("Document");
    }

    [HttpGet]
    public IActionResult Receipt()
    {
        ViewData["DocumentKind"] = "receipt";
        return View("Document");
    }
}
