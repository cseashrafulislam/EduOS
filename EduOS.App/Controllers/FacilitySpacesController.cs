using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesTechnician,FacilitiesApprover")]
[RequireModule("FACILITIES")]
public sealed class FacilitySpacesController : Controller
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
