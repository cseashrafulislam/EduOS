using EduOS.App.Security;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SessionBoundAntiforgeryTests
{
    private static DefaultHttpContext Authenticated(string sid, string? tenant = "100")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "50"),
            new(AuthSessionRegistry.ClaimType, sid)
        };
        if (tenant != null) claims.Add(new Claim("TenantId", tenant));
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"))
        };
    }

    [Fact]
    public void Form_token_is_valid_only_for_its_signed_session_and_institution()
    {
        var provider = new SessionBoundAntiforgeryDataProvider();
        var original = Authenticated(new string('A', 48), "100");
        var token = provider.GetAdditionalData(original);
        Assert.StartsWith("eduos-csrf-session-v1:", token);
        Assert.DoesNotContain(new string('A', 48), token);
        Assert.True(provider.ValidateAdditionalData(original, token));
        Assert.False(provider.ValidateAdditionalData(Authenticated(new string('B', 48), "100"), token));
        Assert.False(provider.ValidateAdditionalData(Authenticated(new string('A', 48), "200"), token));
        Assert.False(provider.ValidateAdditionalData(Authenticated(new string('A', 48), null), token));
        Assert.False(provider.ValidateAdditionalData(Authenticated("invalid", "100"), token));
    }

    [Fact]
    public void Anonymous_login_tokens_cannot_be_replayed_after_sign_in_or_vice_versa()
    {
        var provider = new SessionBoundAntiforgeryDataProvider();
        var anonymous = new DefaultHttpContext();
        var signedIn = Authenticated(new string('C', 48));
        var publicToken = provider.GetAdditionalData(anonymous);
        var privateToken = provider.GetAdditionalData(signedIn);
        Assert.True(provider.ValidateAdditionalData(anonymous, publicToken));
        Assert.False(provider.ValidateAdditionalData(signedIn, publicToken));
        Assert.False(provider.ValidateAdditionalData(anonymous, privateToken));
        Assert.False(provider.ValidateAdditionalData(signedIn, ""));
    }

    [Fact]
    public void Invalid_or_missing_cookie_session_never_validates()
    {
        var provider = new SessionBoundAntiforgeryDataProvider();
        var missing = Authenticated("", "100");
        var malformed = Authenticated(new string('G', 48), "100");
        Assert.False(provider.ValidateAdditionalData(missing, provider.GetAdditionalData(missing)));
        Assert.False(provider.ValidateAdditionalData(malformed, provider.GetAdditionalData(malformed)));
    }
}
