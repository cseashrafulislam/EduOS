using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Source-institution transfer requests and approvals, without cross-tenant conversion.</summary>
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
public sealed class StudentTransferRequestsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
