using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "SuperAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubscriptionPaymentHistoryController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
