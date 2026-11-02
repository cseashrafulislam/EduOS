using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("INVENTORY"), RequireModule("FINANCE")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProcurementSupplierInvoiceMatchingController : Controller
{
    public IActionResult Index() => View();
}
