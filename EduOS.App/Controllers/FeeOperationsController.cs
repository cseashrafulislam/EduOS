using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;

[Authorize(Roles="TenantAdmin,Principal,Accountant")]
[RequireModule("FINANCE")]
public sealed class FeeOperationsController:Controller
{
    [HttpGet]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Index()=>RedirectToAction(nameof(Ledger));

    private IActionResult Feature(string feature)
    {
        ViewData["FeeFeature"] = feature;
        return View("Index");
    }

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Structures()=>Feature("structures");

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Billing()=>Feature("billing");

    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Ledger()=>Feature("ledger");
}
