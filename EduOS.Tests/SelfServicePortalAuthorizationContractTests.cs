using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;
using Xunit;

namespace EduOS.Tests;

public sealed class SelfServicePortalAuthorizationContractTests
{
    [Fact]
    public void Portal_IsRestrictedToStudentGuardianParent_AndRateLimited()
    {
        var authorize = typeof(SelfServicePortalController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Student,Guardian,Parent", authorize!.Roles);
        Assert.NotNull(typeof(SelfServicePortalController).GetCustomAttribute<EnableRateLimitingAttribute>());
    }

    [Theory]
    [InlineData(nameof(SelfServicePortalController.Attendance), "ATTENDANCE")]
    [InlineData(nameof(SelfServicePortalController.Results), "EXAM")]
    [InlineData(nameof(SelfServicePortalController.Fees), "FINANCE")]
    [InlineData(nameof(SelfServicePortalController.Transport), "TRANSPORT")]
    [InlineData(nameof(SelfServicePortalController.Homework), "LMS")]
    [InlineData(nameof(SelfServicePortalController.Assignments), "LMS")]
    public void StudentDataEndpoints_RequireExpectedModule(string action, string module)
    {
        var method = typeof(SelfServicePortalController).GetMethod(action);
        Assert.NotNull(method);
        var requirement = method!.GetCustomAttribute<RequireModuleAttribute>();
        Assert.NotNull(requirement);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + module, requirement!.Policy);
    }

    [Fact]
    public void LinkedStudentDiscovery_DoesNotRequireOperationalModule()
    {
        var method = typeof(SelfServicePortalController).GetMethod(nameof(SelfServicePortalController.Students));
        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<RequireModuleAttribute>());
    }
}
