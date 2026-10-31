using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles="TenantAdmin")]
[RequireModule("FINANCE")]
public sealed class FinanceAdjustmentsController : Controller
{
    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Index()=>View();

    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult HostelClearances()=>View();

    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult TransportClearances()=>View();

    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult ReservationPreflight()=>View();

    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult DiscountAwards()=>View();
}
