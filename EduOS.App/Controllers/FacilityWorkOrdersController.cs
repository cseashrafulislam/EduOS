using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester")]
[RequireModule("FACILITIES")]
public sealed class FacilityWorkOrdersController : Controller
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}