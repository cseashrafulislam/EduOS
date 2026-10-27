using Microsoft.AspNetCore.Localization;

namespace EduOS.App.Localization;

/// <summary>
/// Single allowlisted culture-cookie contract shared by the language selector
/// and successful password/MFA sign-in. No tenant data is stored in the cookie.
/// </summary>
public static class PreferredCultureCookie
{
    public const string DefaultCulture = "bn-BD";

    public static bool TryNormalize(string? value, out string culture)
    {
        if (string.Equals(value, "bn-BD", StringComparison.OrdinalIgnoreCase))
        {
            culture = "bn-BD";
            return true;
        }
        if (string.Equals(value, "en-BD", StringComparison.OrdinalIgnoreCase))
        {
            culture = "en-BD";
            return true;
        }
        culture = DefaultCulture;
        return false;
    }

    public static void Write(HttpContext context, string? preferredLanguage)
    {
        if (!TryNormalize(preferredLanguage, out var culture))
            culture = DefaultCulture;

        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Path = "/"
            });
    }
}
