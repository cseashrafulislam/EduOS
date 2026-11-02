using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize, RequireModule("FACILITIES")]
public sealed class FacilityVisitorsController : Controller
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();
}
