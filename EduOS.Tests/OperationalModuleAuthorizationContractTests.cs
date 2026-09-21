using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class OperationalModuleAuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(LibraryController), "LIBRARY")]
    [InlineData(typeof(TransportController), "TRANSPORT")]
    public void Operational_module_controllers_keep_authenticated_module_antiforgery_and_rate_limit_boundary(Type controllerType, string moduleCode)
    {
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var module = Assert.Single(controllerType.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controllerType.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());

        Assert.Equal(RequireModuleAttribute.PolicyPrefix + moduleCode, module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Student_exit_keeps_student_module_and_mutation_protection_boundary()
    {
        var controllerType = typeof(StudentExitController);
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var module = Assert.Single(controllerType.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "STUDENT", module.Policy);
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));

        var process = controllerType.GetMethod(nameof(StudentExitController.Process))!;
        var post = Assert.Single(process.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        var limiter = Assert.Single(process.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        Assert.Null(post.Template);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.Empty(process.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData(typeof(LibraryController), nameof(LibraryController.Issue), "TenantAdmin,Principal,Librarian")]
    [InlineData(typeof(LibraryController), nameof(LibraryController.Close), "TenantAdmin,Principal,Librarian")]
    [InlineData(typeof(TransportController), nameof(TransportController.Assign), "TenantAdmin,Principal,TransportManager")]
    [InlineData(typeof(TransportController), nameof(TransportController.Close), "TenantAdmin,Principal,TransportManager")]
    public void Operational_mutations_keep_privileged_role_and_post_only_boundary(Type controllerType, string actionName, string expectedRoles)
    {
        var method = controllerType.GetMethods().Single(x => x.Name == actionName);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var post = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());

        Assert.Equal(expectedRoles, authorize.Roles);
        Assert.False(string.IsNullOrWhiteSpace(post.Template));
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
