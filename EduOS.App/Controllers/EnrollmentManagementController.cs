using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

/// <summary>Safe existing-student enrollment screen; authority remains with canonical API/service.</summary>
[Authorize(Roles = "TenantAdmin")]
[RequireModule("ACADEMIC")]
public sealed class EnrollmentManagementController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
