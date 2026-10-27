using System.Security.Claims;
using EduOS.App.Controllers;
using EduOS.App.Localization;
using EduOS.Core.Entities.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class LocalizationPreferenceTests
{
    [Fact]
    public async Task Authenticated_language_selection_is_saved_and_sets_secure_cookie()
    {
        var user = new ApplicationUser { Id = 42, PreferredLanguage = "bn-BD" };
        var (controller, manager) = CreateController(user);
        manager.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await controller.SetLanguage("en-bd", "/Dashboard/Index");

        result.Should().BeOfType<LocalRedirectResult>()
            .Which.Url.Should().Be("/Dashboard/Index");
        user.PreferredLanguage.Should().Be("en-BD");
        manager.Verify(x => x.UpdateAsync(user), Times.Once);
        var cookie = controller.Response.Headers.SetCookie.ToString();
        cookie.Should().Contain(".AspNetCore.Culture=");
        cookie.Should().Contain("en-BD");
        cookie.Should().Contain("httponly");
        cookie.Should().Contain("secure");
        cookie.Should().Contain("samesite=lax");
    }

    [Fact]
    public async Task Anonymous_selection_uses_cookie_without_updating_identity()
    {
        var (controller, manager) = CreateController(null);

        var result = await controller.SetLanguage("bn-BD", "//external.example");

        result.Should().BeOfType<LocalRedirectResult>()
            .Which.Url.Should().Be("/");
        controller.Response.Headers.SetCookie.ToString().Should().Contain("bn-BD");
        manager.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr-FR")]
    [InlineData("en-BD;path=/")]
    public async Task Unsupported_language_is_rejected_without_cookie(string value)
    {
        var user = new ApplicationUser { Id = 42, PreferredLanguage = "bn-BD" };
        var (controller, manager) = CreateController(user);

        (await controller.SetLanguage(value, "/")).Should().BeOfType<BadRequestObjectResult>();
        controller.Response.Headers.SetCookie.ToString().Should().BeEmpty();
        manager.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Concurrent_identity_update_does_not_change_browser_cookie()
    {
        var user = new ApplicationUser { Id = 42, PreferredLanguage = "bn-BD" };
        var (controller, manager) = CreateController(user);
        manager.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));

        (await controller.SetLanguage("en-BD", "/")).Should().BeOfType<ConflictObjectResult>();
        controller.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_or_disabled_identity_cannot_save_language()
    {
        var user = new ApplicationUser { Id = 42, PreferredLanguage = "bn-BD", IsActive = false };
        var (controller, manager) = CreateController(user);

        (await controller.SetLanguage("en-BD", "/")).Should().BeOfType<UnauthorizedResult>();
        controller.Response.Headers.SetCookie.ToString().Should().BeEmpty();
        manager.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public void Sign_in_cookie_writer_rejects_legacy_untrusted_culture_values()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        PreferredCultureCookie.Write(context, "javascript:alert(1)");

        context.Response.Headers.SetCookie.ToString().Should().Contain("bn-BD");
        context.Response.Headers.SetCookie.ToString().Should().NotContain("javascript");
    }

    private static (LocalizationController Controller, Mock<UserManager<ApplicationUser>> Manager)
        CreateController(ApplicationUser? user)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        if (user != null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }, "Test"));
            manager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
            manager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        }

        var urls = new Mock<IUrlHelper>();
        urls.Setup(x => x.IsLocalUrl(It.IsAny<string>()))
            .Returns((string url) => url.StartsWith("/", StringComparison.Ordinal) &&
                !url.StartsWith("//", StringComparison.Ordinal));
        var controller = new LocalizationController(manager.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = urls.Object
        };
        return (controller, manager);
    }
}
