using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

[AllowAnonymous]
[Route("localization")]
public sealed class LocalizationController : Controller
{
    internal static readonly IReadOnlySet<string> SupportedCultures =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "en-BD",
            "bn-BD"
        };

    [HttpPost("set-language")]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        var selectedCulture = SupportedCultures.Contains(culture)
            ? culture
            : "en-BD";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(selectedCulture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/"
            });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
