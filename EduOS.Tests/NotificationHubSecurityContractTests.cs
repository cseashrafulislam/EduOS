using EduOS.App.Hubs;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Xunit;

namespace EduOS.Tests;

public class NotificationHubSecurityContractTests
{
    [Fact]
    public void NotificationHub_RequiresAuthenticatedConnections()
    {
        var authorize = typeof(NotificationHub).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
    }

    [Fact]
    public void SendNotification_IsRestrictedToTenantAdministrators()
    {
        var method = typeof(NotificationHub).GetMethod(nameof(NotificationHub.SendNotification));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("TenantAdmin,Admin", authorize!.Roles);
    }
}
