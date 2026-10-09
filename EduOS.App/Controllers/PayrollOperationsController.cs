using EduOS.App.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers;

[Authorize(Roles="TenantAdmin,Principal,HR,Accountant,Staff,Teacher")]
[RequireModule("PAYROLL")]
public sealed class PayrollOperationsController:Controller
{
 [HttpGet]
 [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
 public IActionResult Index()=>View();
}
