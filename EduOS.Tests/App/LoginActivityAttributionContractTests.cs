using System.Reflection;
using EduOS.App.Controllers.Api;
using Xunit;

namespace EduOS.Tests.App;

public sealed class LoginActivityAttributionContractTests
{
    [Fact]
    public void Authenticated_login_and_mfa_use_selected_institution_for_audit_attribution()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        Assert.Equal(2, source.Split("SaveLoginHistoryAsync(user, ip, userAgent, true, null, session.TenantId)").Length - 1);
        Assert.Contains("SaveLoginHistoryAsync(user, ip, userAgent, false, \"Wrong MFA code\", session.TenantId)", source);
        Assert.Contains("member.TenantId == requestedTenantId.Value", source);
        Assert.Contains("member.UserId == user.Id", source);
        Assert.Contains("member.Status == MembershipStatus.Active", source);
        Assert.Contains("tenant.State == TenantState.Active", source);
        Assert.Contains("if (allowed) tenantId = requestedTenantId.Value", source);
        Assert.Contains("else if (user != null && !isSuccess)", source);
        Assert.DoesNotContain("TenantId = requestedTenantId,", source);
        var writer = typeof(AuthController).GetMethod("SaveLoginHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(writer);
        Assert.Equal(6, writer!.GetParameters().Length);
    }
}
