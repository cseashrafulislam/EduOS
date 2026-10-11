using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin")]
public sealed class TenantUsersController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Accept() => View();

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Terms([FromServices] EduOSDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var document = await db.LegalDocuments.AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted && x.Code.StartsWith("TERMS") && x.EffectiveAt <= now)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id)
            .Select(x => x.Content).FirstOrDefaultAsync(ct);
        return document == null ? NotFound("Current terms are not configured.")
            : Content(document, "text/plain; charset=utf-8");
    }
}
