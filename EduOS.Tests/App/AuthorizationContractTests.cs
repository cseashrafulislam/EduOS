using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EduOS.Tests.App;

public class AuthorizationContractTests
{
    [Fact]
    public void Routed_controller_actions_require_explicit_authorization_or_anonymous_review()
    {
        var controllerBase = typeof(ControllerBase);
        var offenders = typeof(PlatformCatalogController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && controllerBase.IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Instance |
                                           System.Reflection.BindingFlags.Public |
                                           System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(true).OfType<IRouteTemplateProvider>().Any())
                .Select(m => new { Controller = t, Method = m }))
            .Where(x => !HasAuthorizationMetadata(x.Controller) && !HasAuthorizationMetadata(x.Method))
            .Select(x => $"{x.Controller.FullName}.{x.Method.Name}")
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Routed actions without explicit authorization/anonymous review:\n" + string.Join("\n", offenders));
    }

    private static bool HasAuthorizationMetadata(System.Reflection.MemberInfo member)
    {
        var attributes = member.GetCustomAttributes(true);
        return attributes.OfType<IAuthorizeData>().Any() ||
               attributes.OfType<IAllowAnonymous>().Any();
    }
}
