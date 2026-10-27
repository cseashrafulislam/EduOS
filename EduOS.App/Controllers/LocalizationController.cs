using EduOS.App.Localization;
using EduOS.Core.Entities.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[AllowAnonymous]
[Route("localization")]
public sealed class LocalizationController : Controller
{
    private readonly UserManager<ApplicationUser> _users;

    public LocalizationController(UserManager<ApplicationUser> users) => _users = users;

    [HttpPost("set-language")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLanguage(string culture, string? returnUrl)
    {
        Response.Headers.CacheControl = "no-store";
        if (!PreferredCultureCookie.TryNormalize(culture, out var selectedCulture))
            return BadRequest("Unsupported language.");

        // Anonymous visitors can choose a cookie-only language. Authenticated users
        // save their existing global identity preference so a new device can restore it.
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null || !user.IsActive || user.IsDeleted)
                return Unauthorized();

            if (!string.Equals(user.PreferredLanguage, selectedCulture, StringComparison.Ordinal))
            {
                user.PreferredLanguage = selectedCulture;
                user.UpdatedAt = DateTime.UtcNow;
                var updated = await _users.UpdateAsync(user);
                if (!updated.Succeeded)
                    return Conflict("Language preference changed concurrently. Please retry.");
            }
        }

        PreferredCultureCookie.Write(HttpContext, selectedCulture);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
