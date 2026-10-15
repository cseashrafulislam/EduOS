using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantTerminologyController(ITenantSettingService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long? editId = null, CancellationToken cancellationToken = default)
    {
        var response = await service.GetTerminologyAsync(cancellationToken);
        if (!response.Success) return StatusCode(response.StatusCode, response.Message);
        var rows = response.Data?.ToList() ?? new List<TenantTerminologyDto>();
        ViewBag.Edit = editId.HasValue ? rows.FirstOrDefault(x => x.Id == editId.Value) : null;
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] long? id,
        [FromForm] SaveTenantTerminologyRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TerminologyError"] = "Invalid terminology. Check required fields and length limits.";
            return RedirectToAction(nameof(Index), new { editId = id });
        }
        var result = await service.SaveTerminologyAsync(id, request, cancellationToken);
        if (!result.Success)
        {
            TempData["TerminologyError"] = result.StatusCode == 409
                ? "Terminology changed or the key already exists. Reload before retrying."
                : result.Message;
            return RedirectToAction(nameof(Index), new { editId = id });
        }
        TempData["TerminologySuccess"] = "Terminology saved.";
        return RedirectToAction(nameof(Index));
    }
}
