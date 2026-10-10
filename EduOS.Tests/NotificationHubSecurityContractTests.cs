using EduOS.App.Hubs;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Xunit;

namespace EduOS.Tests;

public class NotificationHubSecurityContractTests
{
    [Fact]
    public void NotificationHub_IsMappedThroughAuthenticatedSignalR()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Program.cs"));
        Assert.Contains("builder.Services.AddSignalR(", source, StringComparison.Ordinal);
        Assert.Contains("app.MapHub<NotificationHub>(\"/hubs/notifications\").RequireAuthorization();", source, StringComparison.Ordinal);
        Assert.Contains("app.UseTenantContext();", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("app.UseTenantContext();", StringComparison.Ordinal) <
            source.IndexOf("app.MapHub<NotificationHub>", StringComparison.Ordinal));
    }

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
