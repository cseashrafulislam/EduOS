using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;
[Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
[RequireModule("INVENTORY")]
public sealed class ProcurementSuppliersController : Controller
{
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Index()=>View();
}
