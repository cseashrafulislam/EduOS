using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class AcademicInstructionAuthorizationContractTests
{
    [Fact]
    public void Instruction_controller_keeps_auth_module_antiforgery_rate_limit_and_no_store_boundaries()
    {
        var controller = typeof(AcademicInstructionController);
        var roleBoundary = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Contains("Teacher", roleBoundary.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ACADEMIC", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Empty(controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData(nameof(AcademicInstructionController.CreateSubstitution))]
    [InlineData(nameof(AcademicInstructionController.CancelSubstitution))]
    [InlineData(nameof(AcademicInstructionController.ReviewLessonPlan))]
    public void Manager_only_mutations_are_post_and_privileged(string methodName)
    {
        var method = typeof(AcademicInstructionController).GetMethods().Single(x => x.Name == methodName);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin,Principal,VicePrincipal", authorize.Roles);
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
    }

    [Theory]
    [InlineData(nameof(AcademicInstructionController.CreateLessonPlan))]
    [InlineData(nameof(AcademicInstructionController.UpdateLessonPlan))]
    [InlineData(nameof(AcademicInstructionController.SubmitLessonPlan))]
    [InlineData(nameof(AcademicInstructionController.RecordLessonProgress))]
    public void Teacher_owned_mutations_are_post_and_inherit_authenticated_role_boundary(string methodName)
    {
        var method = typeof(AcademicInstructionController).GetMethods().Single(x => x.Name == methodName);
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
