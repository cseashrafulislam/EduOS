using System.Reflection;
using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Cross-module V2 route security contracts. The list is discovered from compiled API
/// controllers so new LMS, Library, Hostel and Transport endpoints are covered by default.
/// These metadata checks complement, but do not replace, runtime tenant/IDOR tests.
/// </summary>
public sealed class V2ApiPrivacyBoundaryTests
{
    private static readonly string[] ModuleRoutes =
        ["api/lms", "api/library", "api/hostel", "api/transport"];

    public static IEnumerable<object[]> V2ApiControllers()
    {
        return typeof(LmsWorkflowController).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(ControllerBase).IsAssignableFrom(type) &&
                type.Namespace == typeof(LmsWorkflowController).Namespace &&
                type.IsDefined(typeof(ApiControllerAttribute), true))
            .Select(type => (Type: type, Route: type.GetCustomAttribute<RouteAttribute>(true)?.Template))
            .Where(item => item.Route != null &&
                ModuleRoutes.Any(prefix => item.Route.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    item.Route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(item => new object[] { item.Type, item.Route!.Split('/')[1].ToUpperInvariant() })
            .OrderBy(item => ((Type)item[0]).Name);
    }

    [Fact]
    public void V2_api_discovery_covers_all_four_module_families()
    {
        var controllers = V2ApiControllers().ToArray();
        Assert.True(controllers.Length >= 20, "Unexpected loss of V2 API controller coverage.");
        Assert.Equal(new[] { "HOSTEL", "LIBRARY", "LMS", "TRANSPORT" },
            controllers.Select(row => (string)row[1]).Distinct().OrderBy(code => code).ToArray());
    }

    [Theory]
    [MemberData(nameof(V2ApiControllers))]
    public void V2_api_routes_require_entitlement_auth_rate_limit_no_store_and_write_csrf(
        Type controller, string module)
    {
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(true),
            attribute => attribute is not RequireModuleAttribute);
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(true));

        var requiredModule = Assert.Single(controller.GetCustomAttributes<RequireModuleAttribute>(true));
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + module, requiredModule.Policy);

        var limiter = Assert.Single(controller.GetCustomAttributes<EnableRateLimitingAttribute>(true));
        Assert.Equal("ApiPolicy", limiter.PolicyName);

        var cache = Assert.Single(controller.GetCustomAttributes<ResponseCacheAttribute>(true));
        Assert.True(cache.NoStore, controller.Name + " must not cache tenant-owned responses.");
        Assert.Equal(ResponseCacheLocation.None, cache.Location);

        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(true).Any()).ToArray();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(true));
            foreach (var overrideCache in action.GetCustomAttributes<ResponseCacheAttribute>(true))
            {
                Assert.True(overrideCache.NoStore, controller.Name + "." + action.Name);
                Assert.Equal(ResponseCacheLocation.None, overrideCache.Location);
            }

            var changesData = action.GetCustomAttributes<HttpMethodAttribute>(true)
                .SelectMany(attribute => attribute.HttpMethods)
                .Any(verb => !string.Equals(verb, "GET", StringComparison.OrdinalIgnoreCase));
            if (!changesData) continue;

            var csrfProtected =
                controller.IsDefined(typeof(AutoValidateAntiforgeryTokenAttribute), true) ||
                controller.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true) ||
                action.IsDefined(typeof(AutoValidateAntiforgeryTokenAttribute), true) ||
                action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true);
            Assert.True(csrfProtected, controller.Name + "." + action.Name + " needs antiforgery.");
        }
    }

    [Theory]
    [InlineData(typeof(LearningController), "LMS")]
    [InlineData(typeof(EduOS.App.Controllers.LibraryController), "LIBRARY")]
    [InlineData(typeof(HostelOperationsController), "HOSTEL")]
    [InlineData(typeof(TransportOperationsController), "TRANSPORT")]
    [InlineData(typeof(GuardianHostelStaysPrintController), "HOSTEL")]
    public void V2_views_and_print_routes_do_not_cache_private_data(Type controller, string module)
    {
        var gate = Assert.Single(controller.GetCustomAttributes<RequireModuleAttribute>(true));
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + module, gate.Policy);
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(true));

        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpGetAttribute>(true).Any()).ToArray();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var cache = action.GetCustomAttribute<ResponseCacheAttribute>(true) ??
                controller.GetCustomAttribute<ResponseCacheAttribute>(true);
            Assert.NotNull(cache);
            Assert.True(cache.NoStore, controller.Name + "." + action.Name);
            Assert.Equal(ResponseCacheLocation.None, cache.Location);
        }
    }
}
