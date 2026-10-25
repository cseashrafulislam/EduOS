using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubscriptionBillingController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Receipt(long id,
        [FromServices] ISubscriptionPaymentService payments, CancellationToken cancellationToken)
    {
        var result = await payments.GetReceiptAsync(id, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode);
        return View(result.Data);
    }
}
