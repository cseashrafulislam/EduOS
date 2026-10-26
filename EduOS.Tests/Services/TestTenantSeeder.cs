using System.Security.Claims;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;

namespace EduOS.Tests.Services;

/// <summary>
/// Seeds mixed-tenant test fixtures under an unauthenticated bootstrap context.
/// Restores the request tenant before testing the application's security boundary.
/// This bypass is test-only; production tenant write validation remains unchanged.
/// </summary>
internal static class TestTenantSeeder
{
    public static async Task SaveAsync(EduOSDbContext db)
    {
        var http = new HttpContextAccessor().HttpContext
            ?? throw new InvalidOperationException("The test did not initialize an HTTP context.");
        var hadTenant = http.Items.TryGetValue("TenantId", out var tenant);
        var principal = http.User;
        http.Items.Remove("TenantId");
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            await db.SaveChangesAsync();
        }
        finally
        {
            if (hadTenant) http.Items["TenantId"] = tenant;
            http.User = principal;
        }
    }
}
