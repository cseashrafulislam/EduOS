using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[Authorize(Roles = "TenantAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantSettingsController(ITenantSettingService settings) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? category = null, int page = 1, long? editId = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 1000000);
        var response = await settings.GetSettingsAsync(category, page, 20, cancellationToken);
        if (!response.Success) return StatusCode(response.StatusCode, response.Message);
        var result = response.Data ?? new PagedResult<TenantSettingDto>();
        ViewBag.Category = category;
        ViewBag.Selected = editId.HasValue ? result.Items.FirstOrDefault(x => x.Id == editId.Value) : null;
        return View(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] long? id, [FromForm] string? categoryFilter,
        [FromForm] int page, [FromForm] SaveTenantSettingRequestDto request,
        CancellationToken cancellationToken)
    {
        var redirect = new { category = categoryFilter, page = Math.Clamp(page, 1, 1000000), editId = id };
        if (!ModelState.IsValid || request.Value?.Length > 4000)
        {
            TempData["SettingsError"] = "Invalid setting. Check key, category and value length.";
            return RedirectToAction(nameof(Index), redirect);
        }
        if (id.HasValue && string.IsNullOrWhiteSpace(request.RowVersion))
        {
            TempData["SettingsError"] = "Setting version is missing. Reload before editing.";
            return RedirectToAction(nameof(Index), redirect);
        }
        if (!id.HasValue && request.IsSensitive && string.IsNullOrWhiteSpace(request.Value))
        {
            TempData["SettingsError"] = "A protected setting requires an initial value.";
            return RedirectToAction(nameof(Index), redirect);
        }
        var existing = await settings.GetSettingAsync(request.Key, cancellationToken);
        if (!id.HasValue)
        {
            if (existing.Success)
            {
                TempData["SettingsError"] = "This key already exists. Edit the existing setting instead.";
                return RedirectToAction(nameof(Index), redirect);
            }
            if (existing.StatusCode != 404)
            {
                TempData["SettingsError"] = existing.Message;
                return RedirectToAction(nameof(Index), redirect);
            }
        }
        else
        {
            if (!existing.Success || existing.Data?.Id != id.Value)
            {
                TempData["SettingsError"] = "Setting not found. Reload before editing.";
                return RedirectToAction(nameof(Index), redirect);
            }
            if (existing.Data.IsSensitive && !request.IsSensitive)
            {
                TempData["SettingsError"] = "Protected settings cannot be downgraded.";
                return RedirectToAction(nameof(Index), redirect);
            }
            if (!existing.Data.IsSensitive && request.IsSensitive && string.IsNullOrWhiteSpace(request.Value))
            {
                TempData["SettingsError"] = "Provide a value before enabling protection.";
                return RedirectToAction(nameof(Index), redirect);
            }
            if (existing.Data.IsSensitive && request.IsSensitive && string.IsNullOrEmpty(request.Value))
                request.Value = null;
        }
        var saved = await settings.SaveSettingAsync(id, request, cancellationToken);
        if (!saved.Success)
        {
            TempData["SettingsError"] = saved.StatusCode == 409
                ? "Setting changed or conflicts with another update. Reload and retry."
                : saved.Message;
            return RedirectToAction(nameof(Index), redirect);
        }
        TempData["SettingsSuccess"] = "Institution setting saved.";
        return RedirectToAction(nameof(Index), new { category = categoryFilter, page = Math.Clamp(page, 1, 1000000) });
    }
}
