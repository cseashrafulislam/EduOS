using EduOS.App.Controllers.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class PortalResponseCachingContractTests
{
    [Theory]
    [InlineData(typeof(SelfServicePortalController))]
    [InlineData(typeof(EmployeeSelfServiceController))]
    [InlineData(typeof(StudentExitController))]
    [InlineData(typeof(LearnerIdentityController))]
    [InlineData(typeof(LearnerConsentsController))]
    [InlineData(typeof(AuditLogController))]
    [InlineData(typeof(TenantSettingController))]
    [InlineData(typeof(TenantProfileController))]
    public void Sensitive_controllers_disable_client_and_proxy_caching(Type controllerType)
    {
        var attribute = controllerType.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull();
        attribute!.NoStore.Should().BeTrue();
        attribute.Location.Should().Be(ResponseCacheLocation.None);
    }
}
