using EduOS.App.Security;
using Microsoft.AspNetCore.Antiforgery;
using System.Security.Claims;

namespace EduOS.App.Security;

/// <summary>
/// Bind cookie-authenticated form/JSON antiforgery tokens to the signed session and
/// institution. A stale tab must not submit tenant A's form after a cookie switches
/// to tenant B. The token contains a one-way management reference, never the SID.
/// </summary>
public sealed class SessionBoundAntiforgeryDataProvider : IAntiforgeryAdditionalDataProvider
{
    private const string Anonymous = "eduos-csrf-anonymous-v1";
    private const string Invalid = "eduos-csrf-invalid-session-v1";

    public string GetAdditionalData(HttpContext context) => Fingerprint(context) ?? Invalid;

    public bool ValidateAdditionalData(HttpContext context, string additionalData)
    {
        var expected = Fingerprint(context);
        return expected != null &&
            string.Equals(expected, additionalData, StringComparison.Ordinal);
    }

    private static string? Fingerprint(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Anonymous;

        var sid = context.User.FindFirstValue(AuthSessionRegistry.ClaimType);
        if (sid is not { Length: 48 } ||
            sid.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            return null;

        var tenant = context.User.FindFirstValue("TenantId") ?? "-";
        if (tenant != "-" && (!long.TryParse(tenant, out var parsed) || parsed <= 0))
            return null;

        return "eduos-csrf-session-v1:" +
            AuthSessionRegistry.ToPublicReference(sid) + ":" + tenant;
    }
}
