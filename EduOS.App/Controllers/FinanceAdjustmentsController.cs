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

    private IActionResult Feature(string feature)
    {
        ViewData["AdjustmentFeature"] = feature;
        return View("Index");
    }

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Policies()=>Feature("policies");

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult StudentAdjustments()=>Feature("students");

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult FineRegister()=>Feature("fines");

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Refunds()=>Feature("refunds");

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
